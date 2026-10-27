// End-to-end test of every AI-facing tool in the Host half.
// Runs against the packaged ueasset.exe (newest in dist/), same code path DSH uses.
import * as plugin from '../plugin/index.js'
import { Writable } from 'node:stream'
import { existsSync, statSync } from 'node:fs'

const profile = process.argv[2] || 'b1'
const tools = new Map()
const routes = new Map()
const disposers = []

const ctx = {
  effect(fn) { const d = fn(); if (typeof d === 'function') disposers.push(d); return d },
  tools: { register(def) { tools.set(def.name, def); return () => tools.delete(def.name) } },
  webServer: { register(route) { routes.set(route.path, route); return () => routes.delete(route.path) } },
  get(name) {
    if (name === 'attachments') {
      return { saveImage: async ({ data, mediaType, name: imageName }) => ({ attachmentId: 'test-' + Math.random().toString(16).slice(2), mediaType, bytes: data.length, width: 1, height: 1, name: imageName }) }
    }
    return undefined
  }
}
plugin.apply(ctx, {})

const call = (name, args = {}) => {
  const tool = tools.get(name)
  if (!tool) throw new Error('missing tool ' + name)
  return tool.execute(args, {})
}
const routeExact = async (path, method = 'GET', body) => {
  const route = routes.get('/ue-asset/api' + path)
  if (!route) throw new Error('missing route ' + path)
  const payload = body === undefined ? null : Buffer.from(JSON.stringify(body))
  const req = { method, on(ev, h) { if (ev === 'data' && payload) h(payload); if (ev === 'end') h(); } }
  let text = ''
  const res = { setHeader() {}, statusCode: 200, end(b) { text += b } }
  await route.handler(req, res)
  return JSON.parse(text)
}
const routePrefix = async (url) => {
  const route = [...routes.values()].find(r => r.kind === 'prefix' && url.startsWith(r.path))
  if (!route) throw new Error('missing prefix ' + url)
  const chunks = []
  const res = new Writable({ write(c, e, cb) { chunks.push(c); cb() } })
  res.statusCode = 200; res.setHeader = () => {}
  route.handler({ method: 'GET', url }, res)
  await new Promise(r => res.on('finish', r))
  return { status: res.statusCode, bytes: Buffer.concat(chunks) }
}
let failures = 0
const check = (label, ok, detail) => {
  if (ok) console.log('ok   ' + label)
  else { failures++; console.log('FAIL ' + label + (detail === undefined ? '' : ' — ' + JSON.stringify(detail).slice(0, 300))) }
}
const sleep = ms => new Promise(r => setTimeout(r, ms))
const hasNonJson = (value, seen = new Set()) => {
  if (value === undefined) return true
  if (typeof value === 'number' && !Number.isFinite(value)) return true
  if (typeof value === 'bigint' || typeof value === 'function' || typeof value === 'symbol') return true
  if (value && typeof value === 'object') {
    if (seen.has(value)) return true
    seen.add(value)
    return Object.values(value).some(v => hasNonJson(v, seen))
  }
  return false
}

try {
  check('17 tools registered', tools.size === 17, [...tools.keys()])

  const paks = await call('uea_paks', { profile })
  check('uea_paks lists paks', Array.isArray(paks.available) && paks.available.length > 1, { n: paks.available?.length })
  const first = paks.available[0]

  const tagCatalog = await call('uea_get_tag', { profile })
  check('uea_get_tag returns catalog', Array.isArray(tagCatalog.available))

  const before = (tagCatalog.available.find(p => p.name === first.name)?.tags) || []
  await call('uea_set_tag', { profile, pak: first.name, tags: [...before, '_aitest'] })
  const afterSet = await call('uea_get_tag', { profile })
  check('uea_set_tag writes tag', afterSet.available.find(p => p.name === first.name)?.tags.includes('_aitest'))

  const byTag = await call('uea_open_game', { profile, tags: ['_aitest'] })
  check('uea_open_game by tag mounts selected pak', byTag.mounted === 1, { mounted: byTag.mounted })
  await call('uea_sessions', { action: 'close', sessionId: byTag.sessionId })
  await call('uea_set_tag', { profile, pak: first.name, tags: before })

  const needs = await call('uea_open_game', { profile })
  check('uea_open_game asks for paks when no defaults', needs.needsPaks === true && Array.isArray(needs.available) && needs.available.length > 0, { needsPaks: needs.needsPaks, count: needs.available?.length })

  const all = paks.available.map(p => p.name)
  const opened = await call('uea_open_game', { profile, paks: all })
  check('uea_open_game mounts all selected', opened.mounted === all.length, { mounted: opened.mounted, asked: all.length })
  const sid = opened.sessionId

  const sessions = await call('uea_sessions', { action: 'list' })
  check('uea_sessions returns sessions object', Array.isArray(sessions.sessions) && sessions.sessions.some(s => s.sessionId === sid), { keys: Object.keys(sessions) })

  const root = await call('uea_tree', { sessionId: sid, limit: 10 })
  check('uea_tree root', root.total > 0 && root.items.length > 0, { total: root.total })

  const baiyi = await call('uea_tree', { sessionId: sid, path: 'b1/Content/00MainHZ/Characters/Wukong/Meshes/Equip/BaiYi', limit: 30 })
  const meshItem = (baiyi.items || []).find(i => i.name === 'SK_WuKong_body_BaiYi.uasset')
  check('uea_tree exposes files with kind', !!meshItem && meshItem.kind === 'skeletal_mesh', meshItem)

  const found = await call('uea_find', { sessionId: sid, query: 'T_Stag_BaseColor', limit: 5 })
  check('uea_find path search', found.items.length > 0, { total: found.total })

  const textures = await call('uea_find', { sessionId: sid, query: 'T_', kind: 'texture', nameOnly: true, limit: 10 })
  check('uea_find kind=texture nameOnly', textures.items.length > 0 && textures.items.every(i => i.kind === 'texture'), { total: textures.total, sample: textures.items[0] })

  const texture = found.items[0].path
  const inspected = await call('uea_inspect', { sessionId: sid, path: texture, section: 'meta' })
  check('uea_inspect meta', inspected.meta?.className === 'Texture2D', inspected.meta)

  const texPreview = await call('uea_preview', { sessionId: sid, path: texture, maxSize: 128 })
  check('uea_preview texture -> image attachment', texPreview.kind === 'texture' && (texPreview.attachment?.bytes || 0) > 1000, { kind: texPreview.kind, bytes: texPreview.attachment?.bytes })
  check('uea_preview output is lossless JSON (PTC-safe)', !hasNonJson(texPreview), { keys: Object.keys(texPreview) })
  check('uea_preview default does not write disk', texPreview.imagePath === undefined, { imagePath: texPreview.imagePath })

  const savedPreview = await call('uea_preview', { sessionId: sid, path: texture, maxSize: 128, savePng: true })
  check('uea_preview savePng -> temp imagePath', typeof savedPreview.imagePath === 'string' && existsSync(savedPreview.imagePath) && statSync(savedPreview.imagePath).size > 1000, { imagePath: savedPreview.imagePath })

  const meshPreview = await call('uea_preview', { sessionId: sid, path: meshItem.path, maxSize: 256 })
  check('uea_preview mesh', meshPreview.kind === 'mesh' && typeof meshPreview.meta?.modelUrl === 'string', { kind: meshPreview.kind })
  check('mesh has material bindings', (meshPreview.meta?.materialBindings?.length || 0) > 0, { bindings: meshPreview.meta?.materialBindings?.length })
  const glb = await routePrefix(meshPreview.meta.modelUrl)
  check('cache serves GLB', glb.status === 200 && glb.bytes.subarray(0, 4).toString('hex') === '676c5446', { bytes: glb.bytes.length })

  await routeExact('/preview-ack', 'POST', { url: meshPreview.meta.modelUrl, status: 'loaded' })
  const acked = await call('uea_preview', { sessionId: sid, path: meshItem.path, maxSize: 32, waitForClient: true })
  check('uea_preview waits for client ack', acked.clientAck?.status === 'loaded', acked.clientAck)

  const animated = await call('uea_preview', {
    sessionId: sid,
    path: meshItem.path,
    maxSize: 128,
    animationPath: 'b1/Content/00Main/Animation/END_A/SequenceAnim/AS_end_a_wukong_cpt010_seq010_552.uasset'
  })
  const animInfo = animated.meta && animated.meta.animation
  check('uea_preview samples animation', !!animInfo && animInfo.clip?.bones > 0 && animInfo.clip?.samples > 0, animInfo?.clip)
  const animRes = animInfo ? await routePrefix(animInfo.url) : null
  const clip = animRes ? JSON.parse(animRes.bytes.toString()) : null
  check('animation json has tracks', !!clip && Array.isArray(clip.tracks) && clip.tracks.length > 0, { tracks: clip?.tracks?.length })
  if (clip) {
    const glbJsonLength = glb.bytes.readUInt32LE(12)
    const glbJson = JSON.parse(glb.bytes.subarray(20, 20 + glbJsonLength).toString())
    const nodeNames = new Set((glbJson.nodes || []).map(n => n.name))
    const matched = clip.tracks.filter(t => nodeNames.has(t.bone)).length
    check('animation bones match GLB skeleton', matched > 0, { matched, total: clip.tracks.length, sample: clip.tracks.slice(0, 3).map(t => t.bone) })
  }

  const types = await call('uea_asset_types', { action: 'get', profile, sessionId: sid })
  check('uea_asset_types reports registry', types.registry?.loaded === true && types.registry?.count > 1000, types.registry)
  const lod = await call('uea_find', { sessionId: sid, query: 'SKM_LODSettings', nameOnly: true, limit: 1 })
  check('registry classifies LODSettings as data asset', lod.items[0]?.kind === 'data_asset', lod.items[0])
  const savedTypes = await call('uea_asset_types', { action: 'set', profile, rules: [{ prefix: 'ZZ_', kind: 'data_asset' }] })
  check('uea_asset_types saves rules', savedTypes.rules?.some((r) => r.prefix === 'ZZ_'), savedTypes.rules)
  await call('uea_asset_types', { action: 'set', profile, rules: [] })

  const aliasSearch = await call('uea_search_assets', { sessionId: sid, query: '黑熊精' })
  check('uea_search_assets 黑熊精 -> bear mesh', (aliasSearch.items || []).some((item) => /xiong/i.test(item.name) && item.kind === 'skeletal_mesh'), { aliases: aliasSearch.aliases, items: (aliasSearch.items || []).slice(0, 3).map((i) => i.name) })

  const animList = await call('uea_animations', { sessionId: sid, mesh: meshItem.path })
  check('uea_animations lists candidates', Array.isArray(animList) && animList.length > 0, { count: animList?.length })
  check('uea_animations finds the demo clip', (animList || []).some((item) => item.name.startsWith('AS_end_a_wukong_cpt010_seq010_552')), { sample: (animList || []).slice(0, 3).map((i) => i.name) })

  const material = await call('uea_asset', { sessionId: sid, path: 'b1/Content/00MainHZ/Characters/Wukong/Materials/Equip/BaiYi/M_WuKong_BaiYi_Cloth_01_Layers.uasset' })
  check('uea_asset material card', material.card?.type === 'material' && material.card?.textures?.Difuse_Map, { card: material.card?.type, parent: material.card?.parent })
  check('uea_asset material parent chain', (material.card?.parents?.length || 0) > 0, { parents: material.card?.parents })

  const blueprint = await call('uea_asset', { sessionId: sid, path: 'b1/Content/00Main/Animation/Comm/RebirthPoint/ABP_rebirthpoint.uasset' })
  check('uea_asset blueprint card', blueprint.card?.type === 'blueprint' && (blueprint.card?.functions?.length || 0) > 0, { card: blueprint.card?.type, functions: blueprint.card?.functions?.length })
  check('uea_asset anim graph nodes', (blueprint.card?.animGraphNodes?.length || 0) > 0, { nodes: blueprint.card?.animGraphNodes })

  const indexBuild = await call('uea_index', { action: 'build', sessionId: sid })
  check('uea_index build', indexBuild.exists && indexBuild.count > 1000, { count: indexBuild.count, size: indexBuild.sizeBytes })
  const indexSearch = await call('uea_index', { action: 'search', profile, query: 'T_Stag', kind: 'texture', nameOnly: true })
  check('uea_index search', indexSearch.items.length > 0 && indexSearch.items.every(i => i.kind === 'texture'), { total: indexSearch.total })
  const indexStatus = await call('uea_index', { action: 'status', profile })
  check('uea_index status', indexStatus.exists && indexStatus.count === indexBuild.count)
  check('uea_index fresh is not stale', indexStatus.stale === false && indexSearch.stale === false, { status: indexStatus.staleReason, search: indexSearch.staleReason })
  await call('uea_index', { action: 'drop', profile })

  const aclClip = await call('uea_preview', {
    sessionId: sid,
    path: meshItem.path,
    maxSize: 32,
    animationPath: 'b1/Content/00MainHZ/Characters/Wukong/Meshes/Equip/Dujiao/Anim/AS_wukong_body_dujiao.uasset'
  })
  check('ACL animation decodes (natives present)', !!aclClip.meta?.animation?.url && (aclClip.meta.animation.clip?.bones || 0) > 0, aclClip.meta?.animation?.clip || aclClip.meta?.animationError)

  const exportJob = await call('uea_export', { sessionId: sid, paths: [texture] })
  check('uea_export queues job', typeof exportJob.jobId === 'string', exportJob)
  let job = null
  for (let i = 0; i < 60; i++) {
    job = await call('uea_job', { action: 'status', jobId: exportJob.jobId })
    if (['completed', 'failed', 'cancelled'].includes(job.status)) break
    await sleep(500)
  }
  check('uea_job completes export', job?.status === 'completed', job && { status: job.status, error: job.error })
  const files = (job?.results || []).flatMap(r => r.files || [])
  check('export produced file on disk', files.length > 0 && files.every(f => existsSync(f) && statSync(f).size > 0), files)

  const jobs = await call('uea_job', { action: 'list' })
  check('uea_job list', Array.isArray(jobs) && jobs.length > 0, { n: jobs.length })

  const badPak = await call('uea_open_game', { profile, paks: ['__nope__.pak'] }).catch(e => e)
  check('unknown pak -> invalid_argument', badPak instanceof Error, String(badPak))

  const openedAsset = await call('uea_open_asset', { sessionId: sid, path: texture })
  check('uea_open_asset queues preview', openedAsset.queued === true, openedAsset)
  const pending = await routeExact('/pending-open', 'POST', {})
  check('pending-open delivers the asset', pending?.result?.item?.path === texture, pending)

  await call('uea_sessions', { action: 'close', sessionId: sid })
  const after = await call('uea_sessions', { action: 'list' })
  check('uea_sessions close', !after.sessions.some(s => s.sessionId === sid))
} catch (error) {
  failures++
  console.log('FAIL exception — ' + (error && error.stack ? error.stack : error))
} finally {
  for (const d of disposers) { try { await d() } catch {} }
  await sleep(300)
  console.log(failures === 0 ? 'ALL_AI_TOOLS_OK' : 'AI_TOOLS_FAILED ' + failures)
  process.exit(failures === 0 ? 0 : 1)
}