// Integration smoke test for the Host half without a running DSH.
// It mounts plugin/index.js on a minimal Cordis-like context, lets it spawn the real
// `ueasset serve` child, and drives both a tool and an HTTP route.
//
//   node scripts/plugin-smoke.mjs [profile]

import * as plugin from '../plugin/index.js'

const profile = process.argv[2] || 'b1'
const tools = new Map()
const routes = new Map()
const disposers = []
const { Writable } = await import('node:stream')

const ctx = {
  effect(fn) {
    const dispose = fn()
    if (typeof dispose === 'function') disposers.push(dispose)
    return dispose
  },
  tools: {
    register(definition) {
      tools.set(definition.name, definition)
      return () => tools.delete(definition.name)
    }
  },
  webServer: {
    register(route) {
      routes.set(route.path, route)
      return () => routes.delete(route.path)
    }
  },
  get(name) {
    return name === 'attachments' ? undefined : undefined
  }
}

process.env.UEASSET_EXE = 'E:/WorkSpace/Tools/dsh-ue-asset/src/DshUeAsset.Cli/bin/Dist/net10.0/ueasset.dll'
plugin.apply(ctx, {})

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

async function callRoute(path, method = 'GET', body) {
  const route = routes.get('/ue-asset/api' + path)
  if (!route) throw new Error('missing route: ' + path)

  const payload = body === undefined ? null : Buffer.from(JSON.stringify(body))
  const request = {
    method,
    on(event, handler) {
      if (event === 'data' && payload) handler(payload)
      if (event === 'end') handler()
      if (event === 'error') void handler
    },
    destroy() {}
  }

  let status = 0
  let responseBody = ''
  const response = {
    setHeader() {},
    set statusCode(value) { status = value },
    get statusCode() { return status },
    end(body) { responseBody += body }
  }

  await route.handler(request, response)
  return JSON.parse(responseBody)
}

async function callPrefix(url, prefix) {
  const route = [...routes.values()].find((candidate) => candidate.kind === 'prefix' && url.startsWith(candidate.path))
  if (!route) throw new Error('missing prefix route: ' + prefix)

  const chunks = []
  const response = new Writable({ write(chunk, _encoding, callback) { chunks.push(chunk); callback() } })
  response.statusCode = 200
  response.setHeader = () => {}

  const request = { method: 'GET', url }
  route.handler(request, response)
  await new Promise((resolve) => response.on('finish', resolve))
  return { status: response.statusCode, bytes: Buffer.concat(chunks), type: null }
}

let failures = 0
function check(label, condition, detail) {
  if (condition) console.log('ok   ' + label)
  else { failures++; console.log('FAIL ' + label + (detail ? ' — ' + JSON.stringify(detail) : '')) }
}

try {
  check('tools registered', tools.size === 13, [...tools.keys()])
  check('routes registered', routes.size >= 12, [...routes.keys()])

  const ping = await callRoute('/config')
  check('config route', ping.ok === true && Array.isArray(ping.result.profiles), ping)

  if (ping.result.profiles.length > 0) {
    const catalog = await tools.get('uea_paks').execute({ profile }, {})
    check('uea_paks', Array.isArray(catalog.available) && catalog.available.length > 0, { count: catalog.available?.length })

    const chunk0 = catalog.available.find((pak) => pak.chunk === 0)
    if (chunk0) {
      const only = await tools.get('uea_open_game').execute({ profile, paks: [chunk0.name] }, {})
      check('selective open mounts one pak', only.mounted === 1, { mounted: only.mounted })
      await tools.get('uea_sessions').execute({ action: 'close', sessionId: only.sessionId }, {})
    }

    const tagTarget = catalog.available[0]
    const before = tagTarget.tags || []
    const tagged = await tools.get('uea_set_tag').execute({ profile, pak: tagTarget.name, tags: [...before, '_smoke'] }, {})
    check('uea_set_tag', tagged.available.find((p) => p.name === tagTarget.name)?.tags.includes('_smoke'), {})
    await tools.get('uea_set_tag').execute({ profile, pak: tagTarget.name, tags: before }, {})

    const open = await tools.get('uea_open_game').execute({ profile, paks: catalog.available.map((pak) => pak.name) }, {})
    check('uea_open_game', typeof open.sessionId === 'string' && open.mounted > 0, open)

    const tree = await tools.get('uea_tree').execute({ sessionId: open.sessionId, path: 'b1/Content', limit: 5 }, {})
    check('uea_tree', tree.total > 0 && tree.items.length > 0, { total: tree.total })

    const filesTree = await tools.get('uea_tree').execute({
      sessionId: open.sessionId,
      path: 'b1/Content/00MainHZ/Characters/Wukong/Meshes/Equip/BaiYi',
      limit: 30
    }, {})
    check('tree lists files (not only folders)', (filesTree.items || []).some((item) => !item.isDirectory), { total: filesTree.total, sample: (filesTree.items || []).slice(0, 3) })

    const find = await tools.get('uea_find').execute({ sessionId: open.sessionId, query: 'T_Stag_BaseColor', limit: 3 }, {})
    check('uea_find', find.items.length > 0, find)

    const texture = find.items[0].path
    const preview = await tools.get('uea_preview').execute({ sessionId: open.sessionId, path: texture, maxSize: 128 }, {})
    check('uea_preview texture', preview.kind === 'texture' || preview.base64 !== undefined, { kind: preview.kind, className: preview.className })

    const meshes = await tools.get('uea_find').execute({ sessionId: open.sessionId, query: 'SK_whip', limit: 5 }, {})
    const meshPath = (meshes.items || []).find((item) => item.path.endsWith('.uasset'))?.path
    if (meshPath) {
      const mesh = await tools.get('uea_preview').execute({ sessionId: open.sessionId, path: meshPath, maxSize: 256 }, {})
      check('uea_preview mesh', mesh.kind === 'mesh' && typeof mesh.meta?.modelUrl === 'string', { kind: mesh.kind, meta: mesh.meta })
      if (mesh.meta?.modelUrl) {
        const glb = await callPrefix(mesh.meta.modelUrl, '/ue-asset/cache')
        check('cache route serves GLB', glb.status === 200 && glb.bytes.length > 1000 && glb.bytes.subarray(0, 4).toString('hex') === '676c5446', { status: glb.status, bytes: glb.bytes.length })
      }
    } else {
      console.log('skip mesh checks: no SK_whip asset')
    }

    const vendor = await callPrefix('/ue-asset/vendor/three.min.js', '/ue-asset/vendor')
    check('vendor route serves three.js', vendor.status === 200 && vendor.bytes.length > 500_000, { status: vendor.status, bytes: vendor.bytes.length })

    const sessions = await tools.get('uea_sessions').execute({ action: 'list' }, {})
    check('uea_sessions', Array.isArray(sessions) && sessions.length > 0, sessions)
  } else {
    console.log('skip mount checks: no profiles in config')
  }
} catch (error) {
  failures++
  console.error('SMOKE_ERROR', error && error.stack ? error.stack : error)
} finally {
  for (const dispose of disposers) {
    try { await dispose() } catch {}
  }
  await sleep(300)
  console.log(failures === 0 ? 'SMOKE_OK' : 'SMOKE_FAILED ' + failures)
  process.exit(failures === 0 ? 0 : 1)
}