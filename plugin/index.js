// dsh-ue-asset — Host half.
//
// Responsibilities:
//   1. own one `ueasset serve` child (JSON-lines RPC over stdio);
//   2. register the eight DSH-native tools;
//   3. expose the same operations to the Client half over /ue-asset/api/*.

import { spawn } from 'node:child_process'
import { createInterface } from 'node:readline'
import { createReadStream, existsSync, statSync, readdirSync, mkdirSync, writeFileSync, unlinkSync } from 'node:fs'
import { createHash } from 'node:crypto'
import path from 'node:path'
import os from 'node:os'
import { fileURLToPath } from 'node:url'

export const name = 'dsh-ue-asset'
export const inject = ['tools', 'webServer']

const API_PREFIX = '/ue-asset/api'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const PACKAGE_ROOT = path.resolve(__dirname, '..')

function defaultConfigPath() {
  const local = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local')
  return path.join(local, 'dsh-ue-asset', 'config.json')
}

// The package ships in two shapes and the binaries sit in a different place in each:
//   flat install (npm / release tarball):  <pkg>/index.js + <pkg>/dist/ueasset.exe
//   symlinked checkout:                    <repo>/plugin/index.js + <repo>/dist/ueasset.exe
// Reading only the parent directory worked for the checkout and failed for every real
// install, so probe both instead of assuming one layout.
const DIST_ROOTS = [...new Set([path.join(__dirname, 'dist'), path.join(PACKAGE_ROOT, 'dist')])]

function defaultExePath() {
  if (process.env.UEASSET_EXE) return process.env.UEASSET_EXE
  // Newest published exe wins: publishing a new ueasset-<stamp>.exe never collides with
  // the file the currently running serve process holds open.
  for (const dist of DIST_ROOTS) {
    if (!existsSync(dist)) continue
    const candidates = readdirSync(dist)
      .filter((name) => /^ueasset.*\.exe$/i.test(name))
      .map((name) => {
        const full = path.join(dist, name)
        try { return { full, mtime: statSync(full).mtimeMs } } catch { return null }
      })
      .filter(Boolean)
      .sort((a, b) => b.mtime - a.mtime)
    if (candidates.length > 0) return candidates[0].full
  }
  return path.join(PACKAGE_ROOT, 'src', 'DshUeAsset.Cli', 'bin', 'Release', 'net10.0', 'ueasset.dll')
}

function defaultDotnetPath() {
  if (process.env.DOTNET_PATH) return process.env.DOTNET_PATH
  if (process.env.DOTNET_ROOT) {
    const candidate = path.join(process.env.DOTNET_ROOT, 'dotnet.exe')
    if (existsSync(candidate)) return candidate
  }
  const programFiles = process.env.ProgramFiles || 'C:\\Program Files'
  const conventional = path.join(programFiles, 'dotnet', 'dotnet.exe')
  return existsSync(conventional) ? conventional : 'dotnet'
}

class CliProcess {
  constructor(options) {
    this.options = options
    this.child = null
    this.pending = new Map()
    this.nextId = 1
    this.restarts = []
    this.disposed = false
    this.ready = null
  }

  start() {
    if (this.child || this.disposed) return this.ready ?? Promise.resolve()
    const exe = this.options.exePath || defaultExePath()
    const argv = exe.toLowerCase().endsWith('.dll')
      ? [this.options.dotnetPath || 'dotnet', exe, 'serve', '--config', this.options.configPath]
      : [exe, 'serve', '--config', this.options.configPath]

    this.ready = new Promise((resolve, reject) => {
      let settled = false
      const child = spawn(argv[0], argv.slice(1), {
        cwd: path.dirname(exe),
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
        env: { ...process.env }
      })
      this.child = child

      const stdout = createInterface({ input: child.stdout })
      stdout.on('line', (line) => this.handleLine(line))
      child.stderr.on('data', (chunk) => {
        const text = String(chunk).trim()
        if (text) console.error('[ue-asset]', text)
      })

      child.on('spawn', () => {
        settled = true
        resolve()
      })
      child.on('error', (error) => {
        if (!settled) {
          settled = true
          reject(error)
        }
        // A failed spawn must not poison this.ready: every later call would keep
        // resolving the same rejected promise and the plugin would never recover.
        this.ready = null
        console.error('[ue-asset] spawn failed:', error.message)
      })
      child.on('exit', (code, signal) => {
        for (const [, pending] of this.pending) {
          pending.reject(new Error('ueasset serve exited'))
        }
        this.pending.clear()
        this.child = null
        this.ready = null
        if (this.disposed) return
        console.error('[ue-asset] serve exited', { code, signal })
        this.scheduleRestart()
      })
    })

    return this.ready
  }

  scheduleRestart() {
    const now = Date.now()
    this.restarts = this.restarts.filter((t) => now - t < 30_000)
    if (this.restarts.length >= 3) {
      console.error('[ue-asset] giving up after 3 restarts in 30s')
      return
    }
    this.restarts.push(now)
    setTimeout(() => this.start().catch(() => {}), 1500)
  }

  handleLine(line) {
    if (!line.trim()) return
    let message
    try {
      message = JSON.parse(line)
    } catch {
      console.error('[ue-asset] non-JSON stdout:', line.slice(0, 200))
      return
    }
    const pending = this.pending.get(String(message.id))
    if (!pending) return
    this.pending.delete(String(message.id))
    clearTimeout(pending.timer)
    if (message.ok) pending.resolve(message.result)
    else {
      const error = new Error(message.error?.message || 'ueasset request failed')
      error.code = message.error?.code
      error.details = message.error?.details
      pending.reject(error)
    }
  }

  async call(method, params = {}, timeoutMs = 120_000) {
    await this.start()
    if (!this.child) throw new Error('ueasset serve is not running')
    const id = String(this.nextId++)
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id)
        reject(new Error(`ueasset call timed out: ${method}`))
      }, timeoutMs)
      this.pending.set(id, { resolve, reject, timer })
      this.child.stdin.write(JSON.stringify({ id, method, params }) + '\n')
    })
  }

  stop() {
    this.disposed = true
    try { this.child?.kill() } catch {}
    this.child = null
  }
}

export function apply(ctx, config = {}) {
  const options = {
    configPath: config.configPath || defaultConfigPath(),
    // Resolve at spawn time, not apply time: a published dist/ueasset.exe must win after
    // a bundle reload even when this plugin instance was created before it existed.
    exePath: config.exePath || null,
    dotnetPath: config.dotnetPath || defaultDotnetPath()
  }
  const cli = new CliProcess(options)

  ctx.effect(() => {
    cli.start().catch((error) => console.error('[ue-asset] start failed:', error.message))
    return () => cli.stop()
  }, 'dsh-ue-asset: ueasset serve')

  registerTools(ctx, cli)
  registerRoutes(ctx, cli)
}

function textRender(value) {
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2)
  return [{ type: 'text', text: text.length > 60_000 ? text.slice(0, 60_000) + '\n… (truncated)' : text }]
}

function tool(def) {
  return {
    output: { schema: { type: 'object', additionalProperties: true }, render: (_args, value) => textRender(value) },
    isConcurrencySafe: () => false,
    presentCall: (args) => ({ card: 'generic', title: def.name, kind: 'read', rawInput: args }),
    ...def
  }
}

let pendingOpen = null
let pendingOpenSeq = 0
let previewAck = null

function registerTools(ctx, cli) {

  const tools = [
    tool({
      name: 'uea_open_game',
      description: [
        'Mount a local Unreal Engine 4/5 game archive set (PAK / IoStore) and return a sessionId.',
        'Read-only cooked-asset inspection: this plugin does NOT control a running UE editor (use mcp__uemcp__* for that).',
        'Pass a configured profile name, or an ad-hoc paks directory. AES keys and mappings belong in config.json;',
        'prefer profiles so secrets never travel through tool arguments.',
        'Returns mounted reader count, missing-key GUIDs, file count and the detected engine version.'
      ].join('\n'),
      parameters: {
        type: 'object',
        properties: {
          profile: { type: 'string', description: 'Configured game profile name, e.g. "b1".' },
          directory: { type: 'string', description: 'Ad-hoc Paks directory (absolute).' },
          game: { type: 'string', description: 'EGame enum override, e.g. GAME_BlackMythWukong.' },
          usmap: { type: 'string', description: 'Mappings .usmap path for ad-hoc opens.' },
          paks: { type: 'array', items: { type: 'string' }, description: 'Exact pak file names to mount (subset of the archive set).' },
          tags: { type: 'array', items: { type: 'string' }, description: 'Mount every pak carrying one of these tags.' }
        }
      },
      async execute(args) {
        const profile = args?.profile
        const raw = args?.paks
        const wantsAll = raw === 'all' || (Array.isArray(raw) && raw.length === 1 && raw[0] === 'all')
        let paks = Array.isArray(raw) ? raw : undefined

        if (profile && (wantsAll || ((!paks || paks.length === 0) && !(args?.tags && args.tags.length)))) {
          const catalog = await cli.call('paks', { profile })
          if (wantsAll) {
            paks = catalog.available.map((pak) => pak.name)
          } else if (!catalog.defaults || catalog.defaults.length === 0) {
            return {
              needsPaks: true,
              profile,
              available: catalog.available,
              defaults: catalog.defaults || [],
              message: 'profile has no default paks; pass paks:"all", a pak name list, or tags'
            }
          } else {
            paks = catalog.defaults
          }
        }

        const body = { ...(args || {}) }
        if (paks) body.paks = paks
        const summary = await cli.call('open', body, 180_000)
        const warning = summary.needsKey
          ? `Session ${summary.sessionId} mounted ${summary.mounted} archive(s), but ${summary.missing} key GUID(s) are missing; encrypted entries stay unreadable.`
          : `Session ${summary.sessionId}: ${summary.mounted} archive(s), ${summary.fileCount} virtual paths.`
        return { ...summary, summary: warning }
      }
    }),
    tool({
      name: 'uea_paks',
      description: 'List the pak files available for a profile, with size, chunk index and user tags. Pak names are not semantic; use tags to choose.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: { profile: { type: 'string' } },
        required: ['profile']
      },
      async execute(args) { return cli.call('paks', { profile: args?.profile }) }
    }),
    tool({
      name: 'uea_get_tag',
      description: 'Read the pak catalog with tags for a profile.',
      deferLoading: true,
      parameters: { type: 'object', properties: { profile: { type: 'string' } }, required: ['profile'] },
      async execute(args) { return cli.call('getTag', { profile: args?.profile }) }
    }),
    tool({
      name: 'uea_set_tag',
      description: 'Replace the tags of one pak. Tags are stored in config.json and can also be edited in DSH settings.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          profile: { type: 'string' },
          pak: { type: 'string', description: 'Exact pak file name.' },
          tags: { type: 'array', items: { type: 'string' } }
        },
        required: ['profile', 'pak', 'tags']
      },
      async execute(args) { return cli.call('setTag', args) }
    }),
    tool({
      name: 'uea_asset_types',
      description: 'Get or set the per-game filename prefix → asset kind table. Resolution order is AssetRegistry class first, then this table, then built-in conventions. Pass action=get (optionally sessionId for registry status) or action=set with rules [{prefix,kind}].',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          action: { type: 'string', enum: ['get', 'set'], default: 'get' },
          profile: { type: 'string' },
          sessionId: { type: 'string', description: 'Optional: include AssetRegistry load status.' },
          rules: {
            type: 'array',
            items: { type: 'object', properties: { prefix: { type: 'string' }, kind: { type: 'string' } } }
          }
        },
        required: ['profile']
      },
      async execute(args) { return cli.call('assetTypes', args) }
    }),
    tool({
      name: 'uea_animations',
      description: 'List AnimSequence candidates for a skeletal mesh: same folder, sibling Anim/Animations folders, and the character root. Optionally pass prefix/query to search elsewhere.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          mesh: { type: 'string', description: 'Skeletal mesh virtual path.' },
          prefix: { type: 'string', description: 'Optional directory prefix to search instead of the derived folders.' },
          query: { type: 'string', description: 'Optional file-name substring for a broad fallback search.' },
          limit: { type: 'integer', default: 100 }
        },
        required: ['sessionId', 'mesh']
      },
      async execute(args) { return cli.call('animations', args) }
    }),
    tool({
      name: 'uea_asset',
      description: 'Read-only cooked-asset inspection (not UE editor control). Typed definition card for one asset: material parameters/textures, blueprint functions/components, data-table rows, texture/anim metadata. Falls back to a generic card. Use raw:true to include the original propertiesJson.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          path: { type: 'string', description: 'Virtual asset path.' },
          raw: { type: 'boolean', default: false }
        },
        required: ['sessionId', 'path']
      },
      async execute(args) { return cli.call('asset', args) }
    }),
    tool({
      name: 'uea_index',
      description: 'SQLite index over the mounted session paths. action=build (from sessionId) | status | search | drop. Search by name/path substring, optionally kind (texture/material/blueprint/...).',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          action: { type: 'string', enum: ['build', 'status', 'search', 'drop'], default: 'status' },
          sessionId: { type: 'string' },
          profile: { type: 'string' },
          query: { type: 'string' },
          kind: { type: 'string' },
          nameOnly: { type: 'boolean', default: false },
          limit: { type: 'integer', default: 50 },
          offset: { type: 'integer', default: 0 }
        },
        required: ['action']
      },
      async execute(args) { return cli.call('index', args) }
    }),
    tool({
      name: 'uea_sessions',
      description: 'List mounted game sessions, or close one. Closing is refused while that session has running export jobs.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          action: { type: 'string', enum: ['list', 'close'], default: 'list' },
          sessionId: { type: 'string', description: 'Required for action=close.' }
        }
      },
      async execute(args) {
        if ((args?.action ?? 'list') === 'close') {
          if (!args?.sessionId) throw new Error('sessionId is required for action=close')
          await cli.call('close', { sessionId: args.sessionId })
          return { closed: true, sessionId: args.sessionId }
        }
        const list = await cli.call('sessions', {})
        return { sessions: Array.isArray(list) ? list : (list?.sessions ?? []) }
      }
    }),
    tool({
      name: 'uea_tree',
      description: 'List one directory level of the mounted archive: child directories (with subtree counts) and .uasset/.umap files.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          path: { type: 'string', description: 'Virtual directory, e.g. b1/Content. Omit for the root.' },
          limit: { type: 'integer', default: 200, description: 'Max entries per page (1-2000).' },
          offset: { type: 'integer', default: 0 }
        },
        required: ['sessionId']
      },
      async execute(args) {
        return cli.call('tree', args)
      }
    }),
    tool({
      name: 'uea_search_assets',
      description: 'Search assets by a human name (Chinese boss names included, e.g. 黑熊精) and return the best candidates, models first. Alias expansion runs server-side.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          query: { type: 'string', description: 'Human name or filename token, e.g. 黑熊精.' },
          kind: { type: 'string', description: 'Optional kind filter, e.g. skeletal_mesh.' },
          limit: { type: 'integer', default: 50 }
        },
        required: ['sessionId', 'query']
      },
      async execute(args) { return cli.call('searchAssets', args) }
    }),
    tool({
      name: 'uea_find',
      description: 'Search cooked asset paths (not UE editor control). Substring, case-insensitive, inside one session. Returns path, size, extension and kind. Use kind="texture" (or nameOnly=true) to keep results focused; paginate with offset.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          query: { type: 'string', description: 'Substring of the virtual path.' },
          prefix: { type: 'string', description: 'Only paths under this virtual directory.' },
          ext: { type: 'string', description: 'Extension filter, e.g. .uasset or .umap.' },
          kind: { type: 'string', description: 'Asset kind filter: texture | static_mesh | skeletal_mesh | skeleton | material | material_instance | blueprint | anim_blueprint | anim_sequence | anim_montage | data_table | data_asset | world | niagara | particle | sound | asset.' },
          nameOnly: { type: 'boolean', description: 'Match only the file name instead of the full virtual path (much less noisy).' },
          limit: { type: 'integer', default: 100 },
          offset: { type: 'integer', default: 0 }
        },
        required: ['sessionId', 'query']
      },
      async execute(args) {
        return cli.call('find', args)
      }
    }),
    tool({
      name: 'uea_inspect',
      description: 'Inspect one asset package: metadata, export list, and serialized properties as JSON. Use section=meta first for large assets, then section=properties.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          path: { type: 'string', description: 'Virtual path, with or without the .uasset extension. Add .ExportName to target a sub-export.' },
          section: { type: 'string', enum: ['all', 'meta', 'exports', 'properties'], default: 'all' },
          exportLimit: { type: 'integer', default: 100 }
        },
        required: ['sessionId', 'path']
      },
      async execute(args) {
        return cli.call('inspect', args)
      }
    }),
    tool({
      name: 'uea_open_asset',
      description: 'Open one asset in the right sidebar preview (queued; the panel picks it up within ~1.5s). Use this when the user asks to open/show an asset.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          path: { type: 'string', description: 'Virtual asset path.' }
        },
        required: ['sessionId', 'path']
      },
      async execute(args) {
        if (!args?.sessionId || !args?.path) throw new Error('sessionId and path are required')
        pendingOpen = { id: ++pendingOpenSeq, sessionId: args.sessionId, path: args.path, at: Date.now() }
        return { queued: true, id: pendingOpen.id, sessionId: args.sessionId, path: args.path, note: 'sidebar switches to this asset within ~1.5s' }
      }
    }),
    tool({
      name: 'uea_preview',
      description: [
        'Read-only cooked-asset preview (not UE editor control). Texture2D is decoded to a PNG and attached as an image block;',
        'meshes return a GLB (kind=mesh) and other asset types return properties.',
        'If your runtime cannot receive image blocks (run_code / PTC), pass savePng=true and read_image the returned imagePath.'
      ].join('\n'),
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          path: { type: 'string' },
          maxSize: { type: 'integer', default: 1024, description: 'Longest texture edge (64-4096).' },
          savePng: { type: 'boolean', default: false, description: 'Also write the PNG to a temp file and return imagePath. Only needed when image blocks cannot reach your runtime (run_code / PTC); files are pruned automatically.' },
          animationPath: { type: 'string', description: 'Optional UAnimSequence path. When set, the mesh response meta.animation.url points at sampled bone tracks for playback.' },
          waitForClient: { type: 'boolean', default: false, description: 'Wait up to 6s for the sidebar viewer to report whether the GLB actually loaded.' }
        },
        required: ['sessionId', 'path']
      },
      async execute(args, exec) {
        const result = await cli.call('preview', args)

        let clientAck = null
        if (args?.waitForClient && result?.meta?.modelUrl) {
          const target = result.meta.modelUrl
          const deadline = Date.now() + 6000
          while (Date.now() < deadline) {
            if (previewAck && previewAck.url === target) { clientAck = previewAck; break }
            await new Promise((resolve) => setTimeout(resolve, 200))
          }
        }

        if (!result?.base64) {
          if (clientAck) result.clientAck = clientAck
          return result
        }

        const bytes = Buffer.from(result.base64, 'base64')
        let imagePath
        if (args?.savePng === true) {
          try {
            const directory = path.join(os.tmpdir(), 'dsh-ue-asset-previews')
            mkdirSync(directory, { recursive: true })
            // Bounded, self-cleaning: keep the newest 50 files and drop anything older than a day.
            const cutoff = Date.now() - 24 * 60 * 60 * 1000
            const entries = readdirSync(directory)
              .map((name) => { try { const full = path.join(directory, name); return { name, full, mtime: statSync(full).mtimeMs } } catch { return null } })
              .filter((entry) => entry && entry.name.endsWith('.png'))
              .sort((a, b) => b.mtime - a.mtime)
            for (const entry of entries.slice(49)) { try { unlinkSync(entry.full) } catch {} }
            for (const entry of entries) { if (entry.mtime < cutoff) { try { unlinkSync(entry.full) } catch {} } }

            const hash = createHash('sha1').update(String(result.objectPath || '') + '|' + bytes.length).digest('hex').slice(0, 12)
            const safe = path.basename(result.objectPath || 'texture').replace(/[^a-zA-Z0-9._-]/g, '_')
            imagePath = path.join(directory, safe + '-' + hash + '.png')
            writeFileSync(imagePath, bytes)
          } catch (error) {
            console.error('[ue-asset] preview file write failed:', error.message)
          }
        }

        // Strip base64 entirely (a nested `undefined` is not lossless JSON and made the PTC
        // bridge reject the whole tool output). Keep only plain JSON fields.
        const { base64: _base64, ...rest } = result
        const payload = { ...rest }
        if (imagePath) payload.imagePath = imagePath
        if (clientAck) payload.clientAck = clientAck

        const attachments = ctx.get('attachments')
        if (!attachments?.saveImage) {
          payload.note = 'attachments service unavailable; use read_image on imagePath'
          return payload
        }
        const attachment = await attachments.saveImage({
          data: bytes,
          mediaType: result.mime || 'image/png',
          name: path.basename(result.objectPath || 'texture')
        })
        // Build a plain object from known primitive fields instead of trusting the class
        // instance (JSON round-tripping a class with private fields can yield {}).
        const pick = (value) => (value === undefined || value === null ? undefined : value)
        payload.attachment = {
          attachmentId: pick(attachment?.attachmentId),
          mediaType: pick(attachment?.mediaType),
          bytes: pick(attachment?.bytes),
          width: pick(attachment?.width),
          height: pick(attachment?.height),
          name: pick(attachment?.name)
        }
        for (const key of Object.keys(payload.attachment)) {
          if (payload.attachment[key] === undefined) delete payload.attachment[key]
        }
        return payload
      },
      output: {
        schema: { type: 'object', additionalProperties: true },
        render: (_args, value) => {
          const header = `${value.objectPath ?? ''} [${value.className ?? '?'}] ${value.width ?? '?'}×${value.height ?? '?'}`
          const lines = [header]
          if (value.imagePath) lines.push('imagePath: ' + value.imagePath + ' (use read_image if this image block does not reach your runtime)')
          if (value.note) lines.push(value.note)
          if (!value.attachment) return [{ type: 'text', text: lines.join('\n') }]
          return [
            { type: 'text', text: lines.join('\n') },
            { type: 'image', attachment: value.attachment }
          ]
        }
      }
    }),
    tool({
      name: 'uea_export',
      description: 'Queue an export job for one or more assets. Format comes from config (default UEFormat). Returns a jobId; poll with uea_job.',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          sessionId: { type: 'string' },
          paths: { type: 'array', items: { type: 'string' }, description: 'Virtual asset paths.' }
        },
        required: ['sessionId', 'paths']
      },
      async execute(args) {
        if (!Array.isArray(args?.paths) || args.paths.length === 0) throw new Error('paths must be a non-empty array')
        return cli.call('export', args)
      }
    }),
    tool({
      name: 'uea_job',
      description: 'Inspect, list or cancel export jobs. action=status/results return one job; action=list returns every job (optionally filtered by sessionId).',
      deferLoading: true,
      parameters: {
        type: 'object',
        properties: {
          action: { type: 'string', enum: ['status', 'results', 'cancel', 'list'], default: 'list' },
          sessionId: { type: 'string' },
          jobId: { type: 'string', description: 'Required except for action=list.' }
        }
      },
      async execute(args) {
        const action = args?.action ?? 'list'
        if (action === 'list') return cli.call('jobs', { sessionId: args?.sessionId })
        return cli.call('job', { action, sessionId: args?.sessionId, jobId: args?.jobId })
      }
    })
  ]

  for (const definition of tools) {
    ctx.effect(() => ctx.tools.register(definition), `dsh-ue-asset: tool ${definition.name}`)
  }
}

const CONTENT_TYPES = {
  '.glb': 'model/gltf-binary',
  '.gltf': 'model/gltf+json',
  '.bin': 'application/octet-stream',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.json': 'application/json; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8'
}

function resolveInside(root, requestPath) {
  const decoded = decodeURIComponent(requestPath.split('?')[0])
  const candidate = path.resolve(root, '.' + path.posix.sep + decoded)
  const boundary = path.resolve(root) + path.sep
  if (candidate !== path.resolve(root) && !candidate.startsWith(boundary)) return null
  return candidate
}

function serveStatic(req, res, root, urlPrefix) {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    res.statusCode = 405
    res.end()
    return
  }
  const pathname = new URL(req.url ?? '/', 'http://dsh.local').pathname
  const relative = pathname.startsWith(urlPrefix) ? pathname.slice(urlPrefix.length) : ''
  const file = resolveInside(root, relative)
  if (!file || !existsSync(file) || !statSync(file).isFile()) {
    res.statusCode = 404
    res.end('not found')
    return
  }
  res.setHeader('content-type', CONTENT_TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream')
  res.setHeader('cache-control', 'public, max-age=3600')
  if (req.method === 'HEAD') { res.end(); return }
  createReadStream(file).on('error', () => { res.statusCode = 500; res.end() }).pipe(res)
}

function registerRoutes(ctx, cli) {
  const send = (res, status, payload) => {
    const body = JSON.stringify(payload)
    res.statusCode = status
    res.setHeader('content-type', 'application/json; charset=utf-8')
    res.setHeader('cache-control', 'no-store')
    res.end(body)
  }

  const readBody = (req) => new Promise((resolve, reject) => {
    let size = 0
    const chunks = []
    req.on('data', (chunk) => {
      size += chunk.length
      if (size > 4 * 1024 * 1024) {
        reject(new Error('request body too large'))
        req.destroy()
        return
      }
      chunks.push(chunk)
    })
    req.on('end', () => {
      if (chunks.length === 0) return resolve({})
      try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8'))) }
      catch (error) { reject(new Error('invalid JSON body: ' + error.message)) }
    })
    req.on('error', reject)
  })

  const route = (method, sub, rpc, pick = (body) => body) => {
    ctx.effect(() => ctx.webServer.register({
      kind: 'exact',
      path: API_PREFIX + sub,
      handler: async (req, res) => {
        if (req.method !== method) return send(res, 405, { ok: false, error: { code: 'method_not_allowed', message: `use ${method}` } })
        try {
          const body = method === 'GET' ? {} : await readBody(req)
          const result = await cli.call(rpc, pick(body) ?? {})
          send(res, 200, { ok: true, result })
        } catch (error) {
          send(res, 200, { ok: false, error: { code: error.code || 'internal', message: error.message, details: error.details } })
        }
      }
    }), `dsh-ue-asset: route ${sub}`)
  }

  route('GET', '/config', 'config.get')
  route('POST', '/config/global', 'config.saveGlobal')
  route('POST', '/config/profile', 'config.saveProfile')
  route('POST', '/config/delete', 'config.deleteProfile')
  route('POST', '/config/import', 'config.importFmodel')
  route('GET', '/config/games', 'config.games')
  route('POST', '/profile-icon', 'profile.icon')
  route('POST', '/oodle/download', 'oodle.acquire')
  route('POST', '/open', 'open')
  route('GET', '/sessions', 'sessions')
  route('POST', '/close', 'close')
  route('POST', '/tree', 'tree')
  route('POST', '/find', 'find')
  route('POST', '/search-assets', 'searchAssets')
  route('POST', '/inspect', 'inspect')
  route('POST', '/preview', 'preview')
  route('POST', '/export', 'export')
  route('GET', '/jobs', 'jobs')
  route('POST', '/job', 'job')
  route('POST', '/paks', 'paks')
  route('POST', '/asset', 'asset')
  route('POST', '/animations', 'animations')
  route('POST', '/asset-types', 'assetTypes')
  route('POST', '/index', 'index')
  route('POST', '/tag/get', 'getTag')
  route('POST', '/tag/set', 'setTag')
  route('POST', '/pak-keys', 'pakKeys')
  route('POST', '/pak-key', 'bindPakKey')

  ctx.effect(() => ctx.webServer.register({
    kind: 'exact',
    path: API_PREFIX + '/preview-ack',
    handler: async (req, res) => {
      if (req.method !== 'POST') return send(res, 405, { ok: false, error: { code: 'method_not_allowed', message: 'use POST' } })
      try {
        const body = await readBody(req)
        previewAck = { url: body.url, status: body.status || 'loaded', error: body.error || null, at: Date.now() }
        send(res, 200, { ok: true, result: { accepted: true } })
      } catch (error) {
        send(res, 200, { ok: false, error: { code: 'invalid_body', message: error.message } })
      }
    }
  }), 'dsh-ue-asset: preview ack')

  ctx.effect(() => ctx.webServer.register({
    kind: 'exact',
    path: API_PREFIX + '/pending-open',
    handler: (req, res) => {
      if (req.method !== 'POST') return send(res, 405, { ok: false, error: { code: 'method_not_allowed', message: 'use POST' } })
      const item = pendingOpen
      pendingOpen = null
      send(res, 200, { ok: true, result: { item } })
    }
  }), 'dsh-ue-asset: pending open queue')

  // three.js + loaders are vendored under plugin/vendor so previews never reach the network.
  const vendorRoot = path.join(__dirname, 'vendor')
  ctx.effect(() => ctx.webServer.register({
    kind: 'prefix',
    path: '/ue-asset/vendor',
    handler: (req, res) => serveStatic(req, res, vendorRoot, '/ue-asset/vendor')
  }), 'dsh-ue-asset: vendor assets')

  // GLB entries and their dependent textures live under the internal cache. The client only
  // ever receives URLs beneath this prefix; filesystem paths stay on the Host.
  let cacheRoot = null
  const getCacheRoot = async () => {
    const config = await cli.call('config.get', {})
    cacheRoot = config?.cacheRoot || path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'dsh-ue-asset', 'cache')
    return cacheRoot
  }
  ctx.effect(() => ctx.webServer.register({
    kind: 'prefix',
    path: '/ue-asset/cache',
    handler: async (req, res) => {
      try {
        serveStatic(req, res, await getCacheRoot(), '/ue-asset/cache')
      } catch (error) {
        res.statusCode = 500
        res.end(String(error.message))
      }
    }
  }), 'dsh-ue-asset: mesh cache')
}