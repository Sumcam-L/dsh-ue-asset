// Loads plugin/client.js the way the DSH web host does (ModuleLoader + require('react'))
// and drives apply() against a minimal slot context. Components are never rendered; this
// catches load-time and registration-time failures without a browser.

const registrations = []
const React = {
  createElement: (...args) => ({ args }),
  Fragment: 'Fragment',
  useState: (value) => [value, () => {}],
  useEffect: () => {},
  useMemo: (fn) => fn(),
  useRef: (value) => ({ current: value }),
  useCallback: (fn) => fn
}

let clientModule = null
globalThis.window = {
  __ModuleLoader__: {
    load({ id, factory }) {
      clientModule = factory((name) => {
        if (name === 'react') return React
        throw new Error('unexpected require: ' + name)
      })
      clientModule.id = id
    }
  }
}
Object.defineProperty(globalThis, 'navigator', { value: { language: 'zh-CN' }, configurable: true })
globalThis.fetch = async () => ({ json: async () => ({ ok: true, result: [] }) })
globalThis.setTimeout = setTimeout
globalThis.clearTimeout = clearTimeout

await import('../plugin/client.js')

if (!clientModule) throw new Error('client.js did not call __ModuleLoader__.load')
if (typeof clientModule.apply !== 'function') throw new Error('client apply() missing')

const ctx = {
  effect(fn) { return fn() },
  get() { return null },
  slots: {
    inject(_name, callback) { callback() },
    register(spec, component) {
      registrations.push({ spec, component })
      return () => {}
    }
  }
}

clientModule.apply(ctx)

const names = registrations.map((entry) => entry.spec.name)
const required = ['settings.section', 'shell.bottom']
for (const name of required) {
  if (!names.includes(name)) throw new Error('missing slot registration: ' + name)
}
if (registrations.some((entry) => typeof entry.component !== 'function')) {
  throw new Error('a slot component is not a function')
}
console.log('CLIENT_SMOKE_OK registrations=' + registrations.length + ' (' + [...new Set(names)].join(', ') + ')')
process.exit(0)