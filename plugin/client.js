window.__ModuleLoader__.load({
  id: 'dsh-ue-asset',
  factory: (require) => {
    var module = { exports: {} };
    var exports = module.exports;
    Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' });

    const React = require('react');
    const h = React.createElement;
    const { useState, useEffect, useMemo, useRef, useCallback } = React;

    const API = '/ue-asset/api';
    const TAB_ID = 'ue-assets';

    const STRINGS = {
      zh: {
        title: 'UE 资产',
        browse: '浏览',
        preview: '预览',
        search: '搜索资产路径…',
        empty: '还没有打开游戏',
        open: '打开',
        continueLast: '继续上次',
        choose: '选择…',
        loading: '加载中…',
        searching: '搜索中…',
        preparing: '导出预览中…',
        selectHint: '选中一个资产，双击打开预览',
        noPreview: '选择一个资产进行预览',
        noResults: '没有匹配的资产',
        treeEmpty: '这个会话里没有可浏览的内容',
        clearSearch: '清除搜索',
        copyPath: '复制路径',
        allTypes: '全部类型',
        countUnit: '项',
        filterHint: '输入关键词以按类型筛选',
        reload: '重载',
        retry: '重试',
        closeSession: '关闭会话',
        chooseGame: '选择游戏',
        filterPaks: '筛选 PAK…',
        back: '返回',
        sessionClosed: '会话已关闭',
        refresh: '刷新',
        noProfiles: '还没有游戏，先在设置里添加',
        export: '导出',
        queued: '已入队',
        copied: '已复制',
        settings: 'UE资产设置',
        games: '游戏',
        newGame: '+ 新增游戏',
        importFmodel: '从 FModel 导入',
        global: '全局',
        save: '保存',
        remove: '删除',
        name: '名称',
        directory: '主 Paks 目录',
        extra: '附加目录（每行一个）',
        game: '引擎版本（EGame）',
        platform: '纹理平台',
        aes: 'AES 主密钥',
        aesSet: '已设置（留空不修改）',
        aesClear: '清除密钥',
        usmap: 'Mappings (.usmap)',
        outputRoot: '导出目录',
        oodle: 'Oodle DLL',
        meshFormat: '网格格式',
        sessions: '会话上限',
        needsKey: '部分归档缺少 AES 密钥，可关闭会话后在 PAK 列表点锁图标补密钥',
        jobRunning: '导出中',
        details: '属性',
        size: '大小',
        type: '类型',
        close: '关闭',
        reload: '立即重载',
        loadFailed: '请求失败'
      },
      en: {
        title: 'UE Assets',
        browse: 'Browse',
        preview: 'Preview',
        search: 'Search asset paths…',
        empty: 'No game opened yet',
        open: 'Open',
        continueLast: 'Continue',
        choose: 'Choose…',
        loading: 'Loading…',
        searching: 'Searching…',
        preparing: 'Building preview…',
        selectHint: 'Select an asset; double-click to preview',
        noPreview: 'Select an asset to preview',
        noResults: 'No matching assets',
        treeEmpty: 'Nothing to browse in this session',
        clearSearch: 'Clear search',
        copyPath: 'Copy path',
        allTypes: 'All types',
        countUnit: 'items',
        filterHint: 'Type a query to filter by kind',
        reload: 'Reload',
        retry: 'Retry',
        closeSession: 'Close session',
        chooseGame: 'Choose a game',
        filterPaks: 'Filter PAKs…',
        back: 'Back',
        sessionClosed: 'Session closed',
        refresh: 'Refresh',
        noProfiles: 'No games yet; add one in settings.',
        export: 'Export',
        queued: 'Queued',
        copied: 'Copied',
        settings: 'UE asset settings',
        games: 'Games',
        newGame: '+ New game',
        importFmodel: 'Import from FModel',
        global: 'Global',
        save: 'Save',
        remove: 'Delete',
        name: 'Name',
        directory: 'Paks directory',
        extra: 'Extra directories (one per line)',
        game: 'Engine (EGame)',
        platform: 'Texture platform',
        aes: 'AES main key',
        aesSet: 'Set (leave blank to keep)',
        aesClear: 'Clear key',
        usmap: 'Mappings (.usmap)',
        outputRoot: 'Export root',
        oodle: 'Oodle DLL',
        meshFormat: 'Mesh format',
        sessions: 'Session limit',
        needsKey: 'Some archives need AES keys — close the session and click the lock in the PAK list',
        jobRunning: 'Exporting',
        details: 'Properties',
        size: 'Size',
        type: 'Type',
        close: 'Close',
        reload: 'Reload now',
        loadFailed: 'Request failed'
      }
    };
    const lang = (typeof navigator !== 'undefined' && String(navigator.language || '').toLowerCase().startsWith('en')) ? 'en' : 'zh';
    const t = (key) => (STRINGS[lang] && STRINGS[lang][key]) || STRINGS.zh[key] || key;

    async function api(path, body) {
      const options = body === undefined
        ? { method: 'GET' }
        : { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) };
      const response = await fetch(API + path, options);
      const payload = await response.json().catch(() => ({ ok: false, error: { message: 'invalid server response' } }));
      if (!payload.ok) throw new Error((payload.error && payload.error.message) || t('loadFailed'));
      return payload.result;
    }

    function fmtSize(bytes) {
      if (!bytes || bytes < 0) return '';
      if (bytes < 1024) return bytes + ' B';
      if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + 'K';
      if (bytes < 1024 * 1024 * 1024) return (bytes / 1024 / 1024).toFixed(1) + 'M';
      return (bytes / 1024 / 1024 / 1024).toFixed(2) + 'G';
    }

    function useToasts() {
      const [toast, setToast] = useState(null);
      const timer = useRef(null);
      const push = useCallback((message) => {
        setToast(message);
        if (timer.current) clearTimeout(timer.current);
        timer.current = setTimeout(() => setToast(null), 3200);
      }, []);
      useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);
      return { toast, push };
    }

    // Unreal Engine mark (monochrome mask, tinted with currentColor)
    const UE_MARK = 'data:image/svg+xml;base64,CjxzdmcgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIiBpZD0iTGF5ZXJfMSIgdmlld0JveD0iMTc0LjY3IDguMDggMzI0LjM1IDMyNC4zOCI+PHRpdGxlPlVucmVhbCBFbmdpbmUgbG9nbzwvdGl0bGU+PGRlZnM+PHN0eWxlPi5jbHMtMXtmaWxsOiMwMDA7ZmlsbC1ydWxlOmV2ZW5vZGQ7fTwvc3R5bGU+PC9kZWZzPjxwYXRoIGNsYXNzPSJjbHMtMSIgZD0iTTIxOC41Myw1MzIuODlIMTY4LjN2LTQ2LjFoNDkuNDRWNDk3aC0zNnY3Ljg1aDM0LjA4djkuNjNIMTgxLjc2djcuOTRoMzYuNzd2MTAuNDlabTAsMCIvPjxwYXRoIGNsYXNzPSJjbHMtMSIgZD0iTTUwNy41Myw1MzIuODlINDU4LjA5VjQ4Ny41Mmg0OC42N3YxMEg0NzEuMzR2Ny43MmgzMy41NHY5LjQ3SDQ3MS4zNHY3LjgyaDM2LjE5djEwLjMzWm0wLDAiLz48cGF0aCBjbGFzcz0iY2xzLTEiIGQ9Ik0yODYuNjQsNTMyLjg5SDI2Ni4zMmwtMjYuNzYtMzQuNDhoLS4yNnYzNC40OEgyMjZ2LTQ2LjFoMjAuNjVsMjYuNDMsMzQuNTJoLjI2VjQ4Ni43OWgxMy4zM3Y0Ni4xWm0wLDAiLz48cGF0aCBjbGFzcz0iY2xzLTEiIGQ9Ik00NDcuNyw1MzIuODlINDI3LjM4bC0yNi43Ni0zNC40OGgtLjI2djM0LjQ4SDM4N3YtNDYuMWgyMC42NWwyNi40MywzNC41MmguMjZWNDg2Ljc5SDQ0Ny43djQ2LjFabTAsMCIvPjxwYXRoIGNsYXNzPSJjbHMtMSIgZD0iTTMyNS45MSw1MzMuNXEtNi4zOSwwLTExLS4zMkE0MS4xOSw0MS4xOSwwLDAsMSwzMDcsNTMyYTE1LjEsMTUuMSwwLDAsMS01LjI3LTIuNDcsMTAuMzgsMTAuMzgsMCwwLDEtMy4xNi00LjFBMjAuOTMsMjAuOTMsMCwwLDEsMjk3LDUxOS4zYTgwLjQyLDgwLjQyLDAsMCwxLS4zOS04LjUydi0xLjM1YTc4LjM4LDc4LjM4LDAsMCwxLC40MS04LjU4LDIwLjkzLDIwLjkzLDAsMCwxLDEuNTQtNi4xNiwxMC41NSwxMC41NSwwLDAsMSwzLjE3LTQuMDksMTQuNywxNC43LDAsMCwxLDUuMjgtMi40Miw0MS4yMiw0MS4yMiwwLDAsMSw3Ljg5LTEuMTdxNC42NS0uMywxMS0uM2gycTUuMzIsMCw5LjMyLjIzYTQ4LjA4LDQ4LjA4LDAsMCwxLDYuODcuODQsMTYuNzMsMTYuNzMsMCwwLDEsNC43NSwxLjY2LDguNDgsOC40OCwwLDAsMSwzLDIuNjcsMTAuMjUsMTAuMjUsMCwwLDEsMS41MSwzLjg5LDI5LjM4LDI5LjM4LDAsMCwxLC40Miw1LjI5djEuNzdoLTEzdi0uNTZhNi40MSw2LjQxLDAsMCwwLS40OS0yLjY0LDMuNjIsMy42MiwwLDAsMC0xLjkxLTEuNzEsMTMuOTEsMTMuOTEsMCwwLDAtNC0uOXEtMi41OC0uMjUtNi44LS4yNmgtMS4xNHEtMy44MywwLTYuNTQuMTNhMjUsMjUsMCwwLDAtNC41MS41Nyw3LjU0LDcuNTQsMCwwLDAtMi44NSwxLjI3LDQuODksNC44OSwwLDAsMC0xLjU4LDIuMjMsMTMuMjEsMTMuMjEsMCwwLDAtLjY2LDMuNWMtLjEsMS4zOS0uMTUsMy4wNi0uMTUsNXYuODJjMCwxLjkxLjA1LDMuNTQuMTUsNC45YTEyLjg5LDEyLjg5LDAsMCwwLC42NiwzLjQzLDQuODMsNC44MywwLDAsMCwxLjU4LDIuMjEsNy41NCw3LjU0LDAsMCwwLDIuODUsMS4yNywyNS4xMiwyNS4xMiwwLDAsMCw0LjUxLjU3cTIuNzEuMTQsNi41NC4xM2gxLjE0YzIuMDgsMCwzLjg3LDAsNS4zNy0uMTFzMi43Ny0uMiwzLjgtLjM2YTkuNTUsOS41NSwwLDAsMCwyLjQ2LS42OSwzLjc1LDMuNzUsMCwwLDAsMS40My0xLjEyLDMuNjQsMy42NCwwLDAsMCwuNjctMS41NywxMy4wOSwxMy4wOSwwLDAsMCwuMTYtMi4xMnYtMS4yOEgzMjMuNzd2LTguOUgzNTQuM3YxMC4yNGEzNC41NCwzNC41NCwwLDAsMS0uNDMsNS44MywxMi40NywxMi40NywwLDAsMS0xLjU2LDQuMzMsOS4yNyw5LjI3LDAsMCwxLTMsMywxNi4xNiwxNi4xNiwwLDAsMS00LjgzLDEuOTEsNDQuMTEsNDQuMTEsMCwwLDEtNywxYy0yLjcyLjItNS44OC4zLTkuNS4zWm0wLDAiLz48cGF0aCBjbGFzcz0iY2xzLTEiIGQ9Ik0zNzcuMiw1MzNIMzYzLjY5VjQ4Ny4yNEgzNzcuMlY1MzNabTAsMCIvPjxwYXRoIGNsYXNzPSJjbHMtMSIgZD0iTTY0LjcyLDQ2Mi42NHEtOS44LDAtMTcuMTgtLjUzYTc5LjMyLDc5LjMyLDAsMCwxLTEyLjc0LTEuNzksMzAuMzUsMzAuMzUsMCwwLDEtOC44Ny0zLjQ0LDE3LjI5LDE3LjI5LDAsMCwxLTUuNjEtNS40NCwyMS43MiwyMS43MiwwLDAsMS0yLjk1LTcuNzgsNTYuMzMsNTYuMzMsMCwwLDEtLjg2LTEwLjU0di01MmgyMy43djQ0LjMycTAsNC4xMS4yOSw3LjA2YTE3LjA4LDE3LjA4LDAsMCwwLDEuMTcsNSw4LjE0LDguMTQsMCwwLDAsMi41NSwzLjM2LDEyLjM2LDEyLjM2LDAsMCwwLDQuNDEsMiwzMy45LDMzLjksMCwwLDAsNi43NSwxYzIuNjguMTUsNS44Ni4yMyw5LjU3LjIzaDJxNS41NSwwLDkuNTYtLjI2YTM4LjIzLDM4LjIzLDAsMCwwLDYuNzYtLjk1LDExLjg3LDExLjg3LDAsMCwwLDQuNDEtMiw4LjQ4LDguNDgsMCwwLDAsMi41NC0zLjM2LDE2Ljc2LDE2Ljc2LDAsMCwwLDEuMTgtNXEuMjgtMywuMjgtN1YzODEuMTFoMjMuNzF2NTJhNjEuNTIsNjEuNTIsMCwwLDEtLjgsMTAuNTEsMjEuNDksMjEuNDksMCwwLDEtMi44Nyw3LjgxLDE3LjA5LDE3LjA5LDAsMCwxLTUuNTUsNS40NCwzMC4xLDMwLjEsMCwwLDEtOC44NSwzLjQ0LDc5LjM1LDc5LjM1LDAsMCwxLTEyLjc5LDEuNzlxLTcuNDUuNTItMTcuMzUuNTNaIi8+PHBhdGggY2xhc3M9ImNscy0xIiBkPSJNMjM3LjI2LDQ2MS42NUgyMDEuODJsLTQ2LjY2LTYwLjNoLS40NnY2MC4zSDEzMS40NlYzODFoMzZsNDYuMDksNjAuMzZIMjE0VjM4MWgyMy4yNHY4MC42MVptMCwwIi8+PHBhdGggY2xhc3M9ImNscy0xIiBkPSJNMzA3LjEzLDQyMS43MmM0LjA5LDAsNy4zNC0uMTIsOS43Ny0uMzdhMTMuMjIsMTMuMjIsMCwwLDAsNS41Mi0xLjU3LDUuNDYsNS40NiwwLDAsMCwyLjQ2LTMuMjksMjIuMSwyMi4xLDAsMCwwLC41OC01LjU5di0xLjM5YTIxLjc4LDIxLjc4LDAsMCwwLS41OC01LjUyLDUuMiw1LjIsMCwwLDAtMi40OS0zLjI0LDEzLjg3LDEzLjg3LDAsMCwwLTUuNTUtMS41MWMtMi40My0uMjUtNS42Ni0uMzctOS43MS0uMzdIMjc3LjU5djIyLjg1Wm00Mi4yLDM5LjkzSDMyNS45MXYtNi41NGE1NS45LDU1LjksMCwwLDAtLjI4LTYuMDgsMTIuMzcsMTIuMzcsMCwwLDAtMS4yMS00LjMxLDcuMzcsNy4zNywwLDAsMC0yLjU3LTIuODMsMTIuOTQsMTIuOTQsMCwwLDAtNC4zOC0xLjY1LDQyLjU1LDQyLjU1LDAsMCwwLTYuNy0uNzhxLTQtLjIxLTkuNDgtLjIxaC0yMy43djIyLjRIMjU0LjExVjM4MWg2Mi41M2E4OC44MSw4OC44MSwwLDAsMSwxNi4yLDEuMjIsMjMuMTMsMjMuMTMsMCwwLDEsMTAsNC4wOCwxNC43MiwxNC43MiwwLDAsMSw1LjA3LDcuNTgsNDEuNjUsNDEuNjUsMCwwLDEsMS40LDExLjY2djIuODNhNDEuNDksNDEuNDksMCwwLDEtLjc0LDguMzEsMTguMDcsMTguMDcsMCwwLDEtMi40MSw2LjEzLDE0LDE0LDAsMCwxLTQuMjksNC4yNSwyMC40MywyMC40MywwLDAsMS02LjQ3LDIuNTV2LjQ2YTM0LjY1LDM0LjY1LDAsMCwxLDYuOTUsMS43Nyw5LjMzLDkuMzMsMCwwLDEsNC4yNywzLjE4LDEyLjg0LDEyLjg0LDAsMCwxLDIuMTIsNS41Myw1NC42LDU0LjYsMCwwLDEsLjU3LDguNzN2MTIuMzNabTAsMCIvPjxwYXRoIGNsYXNzPSJjbHMtMSIgZD0iTTQ1Mi4zMiw0NjEuNjVoLTg3LjZWMzgxaDg2LjIydjE3LjgzSDM4OC4xOXYxMy43MWg1OS40M3YxNi44NEgzODguMTl2MTMuODloNjQuMTN2MTguMzRabTAsMCIvPjxwYXRoIGNsYXNzPSJjbHMtMSIgZD0iTTUzMS41Miw0MzAuNDFsLTE2LjQzLTMyLjM1aC0uNThMNDk4LjIsNDMwLjQxWk01NzMsNDYxLjcxSDU0Ny40NGwtNy4zOS0xNC40N0g0ODkuNzhsLTcuMjcsMTQuNDdINDU3LjE0bDQzLjI5LTgwLjZoMjkuMDlMNTczLDQ2MS43MVptMCwwIi8+PHBhdGggY2xhc3M9ImNscy0xIiBkPSJNNjU5LjMzLDQ2MS43MUg1ODEuNjl2LTgwLjZoMjMuN1Y0NDNoNTMuOTR2MTguNzVabTAsMCIvPjxwYXRoIGNsYXNzPSJjbHMtMSIgZD0iTTQzNC44MiwxOTVoMGMtMi44MSwxMy41Ni0xNS4zMiw0OC4zNC01NS4xOSw2Ny4xOGwtMTYtMTgtMjcsMjcuMTZhOTkuNzIsOTkuNzIsMCwwLDEtNzguODYtNDAuNDUsMzguODgsMzguODgsMCwwLDAsOC44NCwxLjY2YzQuNDIuMDgsOS4yMS0xLjU0LDkuMjEtOVYxNTAuM2ExMi4wNiwxMi4wNiwwLDAsMC0xNS4xOC0xMi4wN2MtMTIuNSwyLjg2LTIyLjUsMzQuMDctMjIuNSwzNC4wN2E5OS4yNSw5OS4yNSwwLDAsMSwzNC4zNi03Ni4wNkMyOTEuMTIsODAuNDYsMzA5LjMxLDc1LDMyMy4xMyw3Mi44NiwzMDkuNDgsODAuNjQsMzAxLjgsOTMuMzQsMzAxLjgsMTA0YzAsMTcuMDksMTAuMzIsMTUuMDcsMTMuMzgsMTIuNTV2OTguOTJhMTYsMTYsMCwwLDAsMiwzLjQ5QTE0LjU5LDE0LjU5LDAsMCwwLDMyOS4xNSwyMjVjMTAuMzQsMCwyMy43NS0xMS44LDIzLjc1LTExLjh2LTgwLjFjMC04LjE1LTYuMTQtMTgtMTIuMjktMjEuMzYsMCwwLDExLjM4LTIsMjAuMTgsNC43MWgwYTc0LjIsNzQuMiwwLDAsMSw1LjI2LTUuNzlDMzg2LjUxLDkwLjU2LDQwNS44Miw4NC44Niw0MjEuOSw4Mmgwcy0yOS4yNywyMy0yOS4yNyw1My44YzAsMjIuOTMuNTksNzguODQuNTksNzguODRDNDA0LjExLDIyNS4xLDQyMC4yNCwyMTAsNDM0LjgyLDE5NVoiLz48cGF0aCBjbGFzcz0iY2xzLTEiIGQ9Ik0zMzcuOTIsOC40OEExNjMuMDcsMTYzLjA3LDAsMSwwLDUwMSwxNzEuNTQsMTYzLjA2LDE2My4wNiwwLDAsMCwzMzcuOTIsOC40OFptMCwzMTkuNjFBMTU2LjU1LDE1Ni41NSwwLDEsMSw0OTQuNDYsMTcxLjU0LDE1Ni41NSwxNTYuNTUsMCwwLDEsMzM3LjkyLDMyOC4wOVoiLz48L3N2Zz4=';

    const CSS = `
      .ue-root { display:flex; flex-direction:column; height:100%; min-height:0; color:var(--dsw-alias-label-primary); font-size:12px; background:var(--dsw-alias-bg-base); }
      .ue-pane { flex:1; min-height:0; display:flex; flex-direction:column; overflow:auto; }
      .ue-pane[hidden] { display:none; }
      .ue-tabs { display:flex; gap:4px; padding:6px 8px; border-bottom:1px solid var(--dsw-alias-border-l1); }
      .ue-tab { flex:1; padding:5px 8px; border-radius:6px; text-align:center; cursor:pointer; color:var(--dsw-alias-label-secondary); }
      .ue-tab.on { background:var(--dsw-alias-bg-layer-2); color:var(--dsw-alias-label-primary); font-weight:600; }
      .ue-toolbar { display:flex; gap:6px; padding:6px 8px; border-bottom:1px solid var(--dsw-alias-border-l1); align-items:center; }
      .ue-searchwrap { position:relative; flex:1; min-width:0; display:flex; align-items:center; }
      .ue-searchwrap > .ue-search-glyph { position:absolute; left:8px; pointer-events:none; opacity:.5; display:inline-flex; }
      .ue-search { width:100%; min-width:0; padding:5px 26px; border-radius:6px; border:1px solid var(--dsw-alias-border-l1); background:var(--dsw-alias-bg-base); color:inherit; font-size:12px; outline:none; }
      .ue-search:focus { border-color:var(--dsw-alias-brand-primary); }
      .ue-search-clear { position:absolute; right:4px; width:18px; height:18px; padding:0; display:inline-flex; align-items:center; justify-content:center; border:0; border-radius:4px; background:transparent; color:var(--dsw-alias-label-secondary); cursor:pointer; font-size:13px; line-height:1; }
      .ue-search-clear:hover { background:var(--dsw-alias-bg-layer-2); color:var(--dsw-alias-label-primary); }
      .ue-icon-btn { width:24px; height:24px; flex:none; display:inline-flex; align-items:center; justify-content:center; padding:0; border:1px solid transparent; border-radius:6px; background:transparent; color:var(--dsw-alias-label-secondary); cursor:pointer; }
      .ue-icon-btn:hover { background:var(--dsw-alias-bg-layer-2); color:var(--dsw-alias-label-primary); }
      .ue-icon-btn:disabled { opacity:.4; cursor:not-allowed; }
      .ue-select { max-width:120px; padding:4px; border-radius:6px; border:1px solid var(--dsw-alias-border-l1); background:transparent; color:inherit; font-size:11px; }
      .ue-btn { padding:4px 8px; border-radius:6px; border:1px solid var(--dsw-alias-border-l1); background:var(--dsw-alias-bg-layer-2); color:var(--dsw-alias-label-primary); cursor:pointer; font-size:11px; white-space:nowrap; }
      .ue-btn:hover { border-color:var(--dsw-alias-brand-primary); }
      .ue-btn:disabled { opacity:.45; cursor:not-allowed; }
      .ue-btn.primary { background:var(--dsw-alias-button-primary-fill, var(--dsw-alias-brand-primary)); color:var(--dsw-alias-label-primary-foreground, #fff); border-color:transparent; }
      .ue-btn.primary:hover { filter:brightness(1.08); }
      .ue-btn.danger { color:var(--dsw-alias-state-error-primary); }
      .ue-scroll { flex:1; overflow:auto; min-height:0; padding:2px 0 6px; }
      .ue-progress { position:relative; height:2px; flex:none; overflow:hidden; }
      .ue-progress > i { position:absolute; top:0; bottom:0; width:35%; border-radius:2px; background:var(--dsw-alias-brand-primary); animation:ue-slide 1.1s ease-in-out infinite; }
      @keyframes ue-slide { from { left:-35%; } to { left:100%; } }
      .ue-row { display:flex; align-items:center; gap:6px; height:30px; padding:0 8px; cursor:default; border-radius:6px; margin:0 4px; position:relative; }
      .ue-row:hover { background:var(--dsw-alias-bg-layer-2); }
      .ue-row.sel { background:var(--dsw-alias-bg-layer-2); background:color-mix(in srgb, var(--dsw-alias-button-info-fill, #4f8cff) 14%, transparent); }
      .ue-row.sel::before { content:''; position:absolute; left:0; top:6px; bottom:6px; width:2px; border-radius:2px; background:var(--dsw-alias-button-info-fill, #4f8cff); }
      .ue-branch { margin-left:10px; border-left:1px solid var(--dsw-alias-border-l1); }
      .ue-type-icon { flex:0 0 14px; display:inline-flex; align-items:center; justify-content:center; opacity:.9; }
      .ue-caret { flex:0 0 16px; width:16px; height:100%; display:inline-flex; align-items:center; justify-content:center; color:var(--dsw-alias-label-secondary); cursor:pointer; }
      .ue-caret:hover { color:var(--dsw-alias-label-primary); }
      .ue-caret svg { transition:transform .12s ease; }
      .ue-caret.open svg { transform:rotate(90deg); }
      .ue-sub { color:var(--dsw-alias-label-secondary); font-size:10px; opacity:.7; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; max-width:42%; flex:0 1 auto; text-align:right; }
      .ue-name { flex:1; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-size { color:var(--dsw-alias-label-secondary); font-size:11px; font-variant-numeric:tabular-nums; min-width:34px; text-align:right; opacity:.85; }
      .ue-dir { color:var(--dsw-alias-label-primary); font-weight:500; }
      .ue-footer { display:flex; align-items:center; gap:6px; padding:5px 8px; border-top:1px solid var(--dsw-alias-border-l1); background:var(--dsw-alias-bg-base); }
      .ue-footer-main { flex:1; min-width:0; display:flex; flex-direction:column; gap:1px; }
      .ue-footer-name { font-size:12px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-footer-path { font-size:10px; color:var(--dsw-alias-label-secondary); opacity:.72; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-count { display:flex; align-items:center; justify-content:space-between; gap:8px; padding:4px 10px; font-size:11px; color:var(--dsw-alias-label-secondary); border-top:1px solid var(--dsw-alias-border-l1); }
      .ue-empty { display:flex; flex-direction:column; gap:8px; align-items:center; justify-content:center; height:100%; min-height:160px; padding:20px; text-align:center; color:var(--dsw-alias-label-secondary); }
      .ue-empty svg { opacity:.45; }
      .ue-banner { padding:6px 8px; font-size:11px; color:var(--dsw-alias-state-warn-primary); border-bottom:1px solid var(--dsw-alias-border-l1); }
      .ue-preview { display:flex; flex-direction:column; gap:8px; padding:8px 8px 28px; flex:0 0 auto; }
      .ue-canvas { border-radius:8px; background-color:var(--dsw-alias-bg-layer-2); background-image:linear-gradient(45deg, rgba(128,128,128,.12) 25%, transparent 25%, transparent 75%, rgba(128,128,128,.12) 75%), linear-gradient(45deg, rgba(128,128,128,.12) 25%, transparent 25%, transparent 75%, rgba(128,128,128,.12) 75%); background-size:16px 16px; background-position:0 0,8px 8px; display:flex; align-items:center; justify-content:center; min-height:120px; }
      .ue-canvas img { max-width:100%; max-height:320px; image-rendering:auto; }
      .ue-meta { color:var(--dsw-alias-label-secondary); font-size:11px; word-break:break-all; }
      .ue-viewport { position:relative; width:100%; height:280px; border-radius:8px; overflow:hidden; background:var(--dsw-alias-bg-layer-2); }
      .ue-viewport canvas { display:block; width:100% !important; height:100% !important; }
      .ue-viewport-hint { position:absolute; left:8px; bottom:6px; font-size:10px; color:var(--dsw-alias-label-secondary); pointer-events:none; }
      .ue-viewport-badge { position:absolute; right:8px; top:6px; font-size:10px; color:var(--dsw-alias-label-secondary); background:var(--dsw-alias-bg-overlay); padding:2px 6px; border-radius:4px; }
      .ue-anim { display:flex; gap:6px; align-items:center; margin-top:6px; }
      .ue-anim input { flex:1; min-width:0; padding:5px 7px; border-radius:6px; border:1px solid var(--dsw-alias-border-l1); background:var(--dsw-alias-bg-base); color:var(--dsw-alias-label-primary); font-size:12px; }
      .ue-pre { margin:0; padding:8px; background:var(--dsw-alias-bg-layer-2); border-radius:8px; font-size:11px; line-height:1.45; overflow:auto; max-height:320px; white-space:pre-wrap; word-break:break-all; }
      /* Properties JSON used to sit under the preview as a bare dark box with no label; the fold
         gives it a name and keeps a long dump from eating the pane. */
      .ue-props { border:1px solid var(--dsw-alias-border-l1); border-radius:8px; background:var(--dsw-alias-bg-layer-1); padding:0 8px; }
      .ue-props > summary { cursor:pointer; padding:6px 0; font-size:11px; color:var(--dsw-alias-label-secondary); display:flex; gap:6px; align-items:center; list-style:none; }
      .ue-props > summary::-webkit-details-marker { display:none; }
      .ue-props > summary::before { content:'▸'; font-size:9px; line-height:1; }
      .ue-props[open] > summary::before { content:'▾'; }
      .ue-props[open] > summary { border-bottom:1px solid var(--dsw-alias-border-l1); margin-bottom:6px; }
      .ue-props .ue-fold-count { margin-left:auto; opacity:.65; font-variant-numeric:tabular-nums; }
      .ue-pp { display:flex; flex-direction:column; flex:1; min-height:0; }
      .ue-pp-head { display:flex; align-items:center; gap:6px; padding:6px 8px; border-bottom:1px solid var(--dsw-alias-border-l1); flex:none; }
      .ue-pp-info { flex:1; min-width:0; display:flex; flex-direction:column; gap:1px; }
      .ue-pp-title { font-size:11px; font-weight:600; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-pp-path { font-size:10px; color:var(--dsw-alias-label-secondary); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-pp-meta { display:flex; gap:4px; flex-wrap:wrap; align-items:center; }
      .ue-skel { display:flex; flex-direction:column; gap:8px; padding:10px; }
      .ue-skel i { display:block; height:10px; border-radius:5px; background:linear-gradient(90deg, var(--dsw-alias-bg-layer-2) 25%, var(--dsw-alias-bg-layer-1) 37%, var(--dsw-alias-bg-layer-2) 63%); background-size:400% 100%; animation:ue-shimmer 1.4s ease infinite; }
      .ue-skel-label { margin-top:2px; font-size:11px; color:var(--dsw-alias-label-secondary); font-variant-numeric:tabular-nums; }
      .ue-stale { opacity:.45; transition:opacity .12s; pointer-events:none; }
      @keyframes ue-shimmer { 0% { background-position:100% 0; } 100% { background-position:-100% 0; } }
      .ue-games { display:grid; grid-template-columns:repeat(auto-fill, minmax(150px, 1fr)); gap:8px; }
      .ue-game-card { display:flex; align-items:center; gap:8px; padding:8px; border:1px solid var(--dsw-alias-border-l1); border-radius:10px; background:var(--dsw-alias-bg-layer-1); cursor:pointer; min-width:0; }
      .ue-game-card:hover { border-color:var(--dsw-alias-brand-primary); }
      .ue-game-card.on { border-color:var(--dsw-alias-brand-primary); background:color-mix(in srgb, var(--dsw-alias-brand-primary) 12%, transparent); }
      .ue-game-card-body { min-width:0; flex:1; display:flex; flex-direction:column; gap:1px; }
      .ue-game-card-name { font-size:12px; font-weight:600; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-game-card-sub { font-size:10px; color:var(--dsw-alias-label-secondary); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-lp { display:flex; flex-direction:column; flex:1; min-height:0; }
      .ue-lp-head { display:flex; align-items:center; gap:6px; padding:8px 8px 6px; flex:none; font-size:11px; color:var(--dsw-alias-label-secondary); }
      .ue-lp-title { flex:1; min-width:0; font-weight:600; color:var(--dsw-alias-label-primary); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-ue-mark { display:inline-block; width:15px; height:15px; flex:none; margin-right:6px; vertical-align:-3px; background:currentColor; -webkit-mask:url('${UE_MARK}') center/contain no-repeat; mask:url('${UE_MARK}') center/contain no-repeat; opacity:.9; }
      .ue-nav-mark { width:16px; height:16px; margin-right:0; vertical-align:0; opacity:1; }
      .ue-lp-body { flex:1; min-height:0; overflow:auto; padding:0 8px 8px; }
      .ue-lp-sec { font-size:10px; color:var(--dsw-alias-label-secondary); margin:4px 0 6px; letter-spacing:.04em; }
      .ue-sess { display:flex; align-items:center; gap:8px; padding:6px 8px; border:1px solid var(--dsw-alias-border-l1); border-radius:8px; margin-bottom:6px; background:var(--dsw-alias-bg-layer-1); }
      .ue-pak-filter { width:100%; padding:4px 8px; border-radius:6px; border:1px solid var(--dsw-alias-border-l1); background:transparent; color:inherit; font-size:11px; margin-bottom:6px; box-sizing:border-box; }
      .ue-pak { display:flex; align-items:center; gap:6px; padding:3px 4px; border-radius:6px; font-size:11px; flex-wrap:wrap; }
      .ue-pak-main { display:flex; align-items:center; gap:6px; flex:1; min-width:0; cursor:pointer; }
      .ue-pak-lock { font-size:11px; line-height:1; flex:none; padding:2px; border:0; background:transparent; color:var(--dsw-alias-label-secondary); }
      .ue-pak-lock.ok { color:var(--dsw-alias-state-success-primary, #47b881); }
      .ue-pak-lock.warn { color:var(--dsw-alias-state-warning-primary, #d9a441); cursor:pointer; }
      .ue-pak-lock.warn:hover { background:var(--dsw-alias-bg-layer-2); border-radius:4px; }
      .ue-key-row { display:flex; gap:4px; width:100%; padding:2px 0 4px 20px; box-sizing:border-box; }
      .ue-key-input { flex:1; min-width:0; padding:3px 6px; border-radius:6px; border:1px solid var(--dsw-alias-border-l1); background:transparent; color:inherit; font-size:11px; }
      .ue-pak:hover { background:var(--dsw-alias-bg-layer-2); }
      .ue-pak-name { flex:1; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
      .ue-chip { font-size:9px; padding:1px 5px; border-radius:999px; background:var(--dsw-alias-bg-layer-2); color:var(--dsw-alias-label-secondary); flex:none; }
      .ue-lp-foot { display:flex; align-items:center; gap:6px; padding:6px 8px; border-top:1px solid var(--dsw-alias-border-l1); flex:none; }
      .ue-lp-foot .ue-grow { flex:1; min-width:0; font-size:10px; color:var(--dsw-alias-label-secondary); font-variant-numeric:tabular-nums; }
      .ue-section { padding:10px 12px; border-bottom:1px solid var(--dsw-alias-border-l1); }
      .ue-section h4 { margin:0 0 8px; font-size:12px; font-weight:600; }
      .ue-field { display:flex; flex-direction:column; gap:4px; margin-bottom:8px; }
      .ue-field label { color:var(--dsw-alias-label-secondary); font-size:11px; }
      .ue-field input, .ue-field select, .ue-field textarea { padding:5px 7px; border-radius:6px; border:1px solid var(--dsw-alias-border-l1); background:var(--dsw-alias-bg-layer-1, var(--dsw-alias-bg-base)); color:var(--dsw-alias-label-primary); font-size:12px; outline:none; font-family:inherit; }
      /* Native select popups ignore the parent background; without a dark color-scheme the
         options render white-on-white in the dark theme. */
      body[data-ds-dark-theme] .ue-root select { color-scheme: dark; }
      body[data-ds-dark-theme] .ue-root select option { background-color: #24262b; color: #f2f3f5; }
      body[data-ds-dark-theme] .ue-root select option:disabled { color: #8b8f96; }
      .ue-modal-backdrop { position:fixed; inset:0; background:rgba(0,0,0,.45); z-index:60; display:flex; align-items:center; justify-content:center; }
      .ue-modal { width:min(680px, calc(100vw - 80px)); max-height:70vh; display:flex; flex-direction:column; gap:6px; background:var(--dsw-alias-bg-layer-1, #1b1d21); border:1px solid var(--dsw-alias-border-l1); border-radius:10px; padding:10px; }
      .ue-modal-head { display:flex; align-items:center; justify-content:space-between; font-weight:600; }
      .ue-modal-list { overflow:auto; max-height:45vh; border-top:1px solid var(--dsw-alias-border-l1); padding-top:4px; }
      .ue-game { border:1px solid var(--dsw-alias-border-l1); border-radius:10px; margin-bottom:8px; background:var(--dsw-alias-bg-layer-1, transparent); }
      .ue-game > summary { cursor:pointer; padding:8px 10px; font-weight:600; list-style:none; display:flex; align-items:center; gap:6px; position:relative; }
      .ue-game > summary:focus { outline:none; }
      .ue-game[open] > summary::after { content:''; position:absolute; left:0; top:9px; bottom:9px; width:2px; border-radius:2px; background:var(--dsw-alias-button-info-fill, #4f8cff); }
      .ue-game > summary::-webkit-details-marker { display:none; }
      .ue-game-icon { width:20px; height:20px; border-radius:5px; object-fit:contain; flex:none; background:rgba(128,128,128,.14); }
      .ue-game-icon.placeholder { display:inline-flex; align-items:center; justify-content:center; font-size:11px; font-weight:600; color:var(--dsw-alias-label-secondary); text-transform:uppercase; }
      .ue-game > summary:before { content:'▸'; color:var(--dsw-alias-label-secondary); }
      .ue-game[open] > summary:before { content:'▾'; }
      .ue-game-body { padding:0 10px 10px; }
      .ue-scroll { max-height:180px; overflow:auto; border:1px solid var(--dsw-alias-border-l1); border-radius:8px; padding:6px; }
      .ue-fold { border:1px solid var(--dsw-alias-border-l1); border-radius:8px; margin-bottom:8px; }
      .ue-fold > summary { cursor:pointer; padding:7px 10px; font-size:12px; color:var(--dsw-alias-label-secondary); list-style:none; display:flex; align-items:center; gap:6px; }
      .ue-fold > summary::-webkit-details-marker { display:none; }
      .ue-fold > summary:focus { outline:none; }
      .ue-fold > summary:before { content:'▸'; font-size:10px; }
      .ue-fold[open] > summary:before { content:'▾'; }
      .ue-fold-count { margin-left:auto; font-size:10px; font-variant-numeric:tabular-nums; background:var(--dsw-alias-bg-layer-2, rgba(127,127,127,.18)); border-radius:999px; padding:1px 7px; }
      .ue-fold-body { padding:0 10px 10px; }
      .ue-actions { display:flex; gap:6px; align-items:center; margin-top:12px; padding-top:10px; border-top:1px solid var(--dsw-alias-border-l1); }
      .ue-field input:focus, .ue-field textarea:focus { border-color:var(--dsw-alias-brand-primary); }
      .ue-list { display:flex; flex-direction:column; gap:2px; margin-bottom:8px; }
      .ue-list-item { padding:6px 8px; border-radius:6px; cursor:pointer; }
      .ue-list-item:hover { background:var(--dsw-alias-bg-layer-2); }
      .ue-list-item.on { background:var(--dsw-alias-bg-layer-2); box-shadow:inset 2px 0 0 var(--dsw-alias-button-info-fill, #4f8cff); }
      .ue-toast { position:sticky; bottom:0; padding:6px 10px; background:var(--dsw-alias-bg-overlay); border-top:1px solid var(--dsw-alias-border-l1); font-size:11px; }
      .ue-jobs { display:flex; align-items:center; gap:8px; padding:2px 10px; font-size:11px; color:var(--dsw-alias-label-secondary); }
      .ue-bar { height:4px; border-radius:2px; background:var(--dsw-alias-bg-layer-2); flex:1; overflow:hidden; }
      .ue-bar > i { display:block; height:100%; background:var(--dsw-alias-brand-primary); }
      .ue-card { padding:8px 10px; border:1px solid var(--dsw-alias-border-l1); border-radius:8px; margin:4px 0; font-size:12px; }
      .ue-card .ue-meta { margin-top:2px; }
      .ue-status { display:inline-block; width:6px; height:6px; border-radius:50%; margin-right:4px; background:var(--dsw-alias-state-idle-primary); }
      .ue-status.ok { background:var(--dsw-alias-state-success-primary); }
      .ue-status.warn { background:var(--dsw-alias-state-warn-primary); }
      .ue-hint { color:var(--dsw-alias-label-secondary); font-size:11px; line-height:1.5; }
    `;

    function installStyle() {
      if (typeof document === 'undefined') return () => {};
      const style = document.createElement('style');
      style.dataset.plugin = 'dsh-ue-asset';
      style.textContent = CSS;
      document.head.appendChild(style);
      return () => style.remove();
    }

    // ── Browse tree ─────────────────────────────────────────────────────────

    const ICON_PATHS = {
      folder: ['M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'],
      texture: ['M4 5h16v14H4z', 'M4 15l4-4 4 4 3-3 5 5', 'M8 9h.01'],
      static_mesh: ['M12 3l8 4.5v9L12 21l-8-4.5v-9z', 'M12 12l8-4.5', 'M12 12v9', 'M12 12L4 7.5'],
      skeletal_mesh: ['M12 3a2.4 2.4 0 1 0 0 4.8 2.4 2.4 0 0 0 0-4.8z', 'M12 7.8v5.8', 'M12 9.2l-4 2.2', 'M12 9.2l4 2.2', 'M12 13.6l-3.1 6.6', 'M12 13.6l3.1 6.6', 'M8.9 20.2h1.8', 'M13.3 20.2h1.8'],
      skeleton: ['M17 10c.7-.7 1.69 0 2.5 0a2.5 2.5 0 1 0 0-5 .5.5 0 0 1-.5-.5 2.5 2.5 0 1 0-5 0c0 .81.7 1.8 0 2.5l-7 7c-.7.7-1.69 0-2.5 0a2.5 2.5 0 0 0 0 5c.28 0 .5.22.5.5a2.5 2.5 0 1 0 5 0c0-.81-.7-1.8 0-2.5Z'],
      material: ['M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z', 'M8 9a5 5 0 0 1 5-4'],
      material_instance: ['M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z', 'M8 9a5 5 0 0 1 5-4', 'M18 17a1 1 0 1 1-.01 0'],
      blueprint: ['M4 4h6v6H4z', 'M14 14h6v6h-6z', 'M10 7h4v7'],
      anim_blueprint: ['M4 4h6v6H4z', 'M14 14h6v6h-6z', 'M10 7h4v4', 'M16 15.5l3 1.5-3 1.5z'],
      anim_sequence: ['M8 5v14l11-7z'],
      anim_montage: ['M8 5v14l11-7z', 'M4 5v14'],
      sound: ['M4 10v4h3l4 3V7L7 10H4z', 'M15 9a4 4 0 0 1 0 6', 'M17.5 7a7 7 0 0 1 0 10'],
      data_table: ['M4 5h16v14H4z', 'M4 10h16', 'M4 15h16', 'M10 5v14'],
      data_asset: ['M5 6c0-1.7 3.1-3 7-3s7 1.3 7 3-3.1 3-7 3-7-1.3-7-3z', 'M5 6v12c0 1.7 3.1 3 7 3s7-1.3 7-3V6'],
      world: ['M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z', 'M3 12h18', 'M12 3c3 3 3 15 0 18', 'M12 3c-3 3-3 15 0 18'],
      niagara: ['M12 3l1.8 6.2L20 11l-6.2 1.8L12 19l-1.8-6.2L4 11l6.2-1.8z', 'M18 4l.6 2.4L21 7l-2.4.6L18 10l-.6-2.4L15 7l2.4-.6z'],
      particle: ['M6 17a4 4 0 1 1 1-7 5 5 0 0 1 9 0 4 4 0 0 1 2 7z', 'M8 20h.01', 'M12 21h.01', 'M16 20h.01'],
      asset: ['M6 3h8l4 4v14H6z', 'M14 3v4h4']
    };
    const ICON_COLORS = {
      folder: '#d9a441', texture: '#e0a458', static_mesh: '#7aa2f7', skeletal_mesh: '#9ece6a',
      skeleton: '#9ece6a', material: '#bb9af7', material_instance: '#bb9af7', blueprint: '#7dcfff',
      anim_blueprint: '#7dcfff', anim_sequence: '#f7768e', anim_montage: '#f7768e', sound: '#e0af68',
      data_table: '#a9b1d6', data_asset: '#a9b1d6', world: '#73daca', niagara: '#f7768e',
      particle: '#f7768e', asset: '#8b8f96'
    };
    function TypeIcon(props) {
      const kind = props.kind || 'asset'
      const paths = ICON_PATHS[kind] || ICON_PATHS.asset
      return h('span', { className: 'ue-type-icon', style: { color: ICON_COLORS[kind] || ICON_COLORS.asset }, title: kind },
        h('svg', { viewBox: '0 0 24 24', width: 14, height: 14, fill: 'none', stroke: 'currentColor', strokeWidth: 1.7, strokeLinecap: 'round', strokeLinejoin: 'round' },
          paths.map((d, index) => h('path', { key: index, d }))))
    }

    function TreeRow(props) {
      const { node, depth, isOpen, selected, onSelect, onOpen, onToggle } = props;
      const dir = node.isDirectory;
      return h('div', {
        className: 'ue-row' + (selected === node.path ? ' sel' : ''),
        style: { paddingLeft: 6 + depth * 12 },
        title: dir ? node.path : node.path + '\n单击预览',
        onClick: () => {
          onSelect(node)
          if (!dir) onOpen(node)
        },
        onDoubleClick: () => { if (dir) onToggle(node); else onOpen(node); }
      },
        dir
          ? h('span', { className: 'ue-caret' + (isOpen ? ' open' : ''), onClick: (event) => { event.stopPropagation(); onToggle(node); } },
              h('svg', { viewBox: '0 0 24 24', width: 12, height: 12, fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' }, h('path', { d: 'M9 6l6 6-6 6' })))
          : h('span', { className: 'ue-caret' }, ''),
        h(TypeIcon, { kind: dir ? 'folder' : (node.kind || 'asset') }),
        h('span', { className: 'ue-name' + (dir ? ' ue-dir' : '') }, node.name),
        node.subtitle ? h('span', { className: 'ue-sub' }, node.subtitle) : null,
        h('span', { className: 'ue-size' }, dir ? (node.count ? node.count.toLocaleString() : '') : fmtSize(node.size))
      );
    }

    function Tree(props) {
      const { nodes, depth, expanded, selected, onSelect, onOpen, onToggle } = props;
      return h('div', null, nodes.map((node) => {
        const open = expanded.has(node.path);
        return h(React.Fragment, { key: node.path },
          h(TreeRow, { node, depth, isOpen: open, selected, onSelect, onOpen, onToggle }),
          node.isDirectory && open && props.childrenOf(node.path)
            ? h('div', { className: 'ue-branch' }, h(Tree, { ...props, nodes: props.childrenOf(node.path), depth: depth + 1 }))
            : null
        );
      }));
    }

    const KIND_FILTERS = [
      ['texture', '贴图'], ['static_mesh', '静态网格'], ['skeletal_mesh', '骨骼网格'],
      ['skeleton', '骨架'], ['material', '材质'], ['blueprint', '蓝图'],
      ['anim_sequence', '动画'], ['sound', '音频'], ['data_table', '数据表'],
      ['data_asset', '数据资产'], ['world', '关卡']
    ]

    function EmptyGlyph() {
      return h('svg', { viewBox: '0 0 24 24', width: 28, height: 28, fill: 'none', stroke: 'currentColor', strokeWidth: 1.4, strokeLinecap: 'round', strokeLinejoin: 'round' },
        h('path', { d: 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z' }),
        h('path', { d: 'M8 13h8' }))
    }

    function IconRefresh(props) {
      return h('svg', { viewBox: '0 0 24 24', width: 13, height: 13, fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' },
        h('path', { d: 'M20 11a8 8 0 1 0-2.34 5.66' }), h('path', { d: 'M20 5v6h-6' }))
    }

    function IconCopy(props) {
      return h('svg', { viewBox: '0 0 24 24', width: 13, height: 13, fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' },
        h('rect', { x: 9, y: 9, width: 11, height: 11, rx: 2 }), h('path', { d: 'M5 15V5a2 2 0 0 1 2-2h10' }))
    }

    function IconClose(props) {
      return h('svg', { viewBox: '0 0 24 24', width: 13, height: 13, fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' },
        h('path', { d: 'M6 6l12 12' }), h('path', { d: 'M18 6L6 18' }))
    }

    function BrowseTab(props) {
      const { sessionId, onOpenAsset, pushToast } = props;
      const [root, setRoot] = useState([]);
      const [children, setChildren] = useState({});
      const [expanded, setExpanded] = useState(new Set());
      const [selected, setSelected] = useState(null);
      const [query, setQuery] = useState('');
      const [results, setResults] = useState(null);
      const [resultTotal, setResultTotal] = useState(0);
      const [searched, setSearched] = useState({ query: '', kind: 'all' });
      const [truncated, setTruncated] = useState(false);
      const [error, setError] = useState(null);
      const [loading, setLoading] = useState(false);
      const [kind, setKind] = useState('all');
      const [tick, setTick] = useState(0);

      useEffect(() => {
        setSelected(null); setExpanded(new Set()); setChildren({}); setQuery(''); setResults(null); setKind('all');
      }, [sessionId]);

      useEffect(() => {
        setLoading(true); setError(null);
        api('/tree', { sessionId, path: '', limit: 500 })
          .then((page) => setRoot(page.items || []))
          .catch((e) => setError(e.message))
          .finally(() => setLoading(false));
      }, [sessionId, tick]);

      useEffect(() => {
        if (!query.trim()) { setResults(null); setTruncated(false); setResultTotal(0); setSearched({ query: '', kind }); return; }
        let cancelled = false;
        const timer = setTimeout(() => {
          setLoading(true);
          api('/find', { sessionId, query: query.trim(), limit: 200, ...(kind === 'all' ? {} : { kind }) })
            .then((page) => {
              if (cancelled) return;
              setResults(page.items || []);
              setResultTotal(page.total || 0);
              setTruncated(page.nextOffset != null);
              setSearched({ query: query.trim(), kind });
            })
            .catch((e) => { if (!cancelled) setError(e.message); })
            .finally(() => { if (!cancelled) setLoading(false); });
        }, 300);
        return () => { cancelled = true; clearTimeout(timer); };
      }, [query, sessionId, kind]);

      const loadChildren = useCallback(async (path) => {
        setLoading(true);
        try {
          const page = await api('/tree', { sessionId, path, limit: 500 });
          setChildren((prev) => ({ ...prev, [path]: page.items || [] }));
        } catch (e) {
          setError(e.message);
        } finally {
          setLoading(false);
        }
      }, [sessionId]);

      const toggle = useCallback((node) => {
        setExpanded((prev) => {
          const next = new Set(prev);
          if (next.has(node.path)) next.delete(node.path);
          else next.add(node.path);
          return next;
        });
        if (!children[node.path]) loadChildren(node.path);
      }, [children, loadChildren]);

      // Results shown belong to an older query/filter while a new one is in flight — dim them
      // instead of silently presenting a mix the user never asked for.
      const staleResults = results !== null && (searched.query !== query.trim() || searched.kind !== kind);
      const rows = results !== null ? results : root;
      const childrenOf = (path) => children[path];
      const select = (node) => setSelected(node.path);
      const open = (node) => {
        setSelected(node.path);
        if (node.isDirectory) { toggle(node); return; }
        onOpenAsset(node.path);
      };

      return h('div', { className: 'ue-root', style: { height: '100%' } },
        h('div', { className: 'ue-toolbar' },
          h('div', { className: 'ue-searchwrap' },
            h('span', { className: 'ue-search-glyph' },
              h('svg', { viewBox: '0 0 24 24', width: 13, height: 13, fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round' },
                h('circle', { cx: 11, cy: 11, r: 6 }), h('path', { d: 'M20 20l-3.6-3.6' }))),
            h('input', {
              className: 'ue-search',
              placeholder: t('search'),
              value: query,
              onChange: (event) => setQuery(event.target.value),
              onKeyDown: (event) => {
                if (event.key === 'Escape') { setQuery(''); return }
                if (event.key === 'Enter' && results) {
                  const first = results.find((node) => !node.isDirectory)
                  if (first) { select(first); onOpenAsset(first.path) }
                }
              }
            }),
            query ? h('button', { className: 'ue-search-clear', title: t('clearSearch'), onClick: () => setQuery('') }, '×') : null
          ),
          h('select', {
            className: 'ue-select',
            value: kind,
            title: '类型筛选',
            onChange: (event) => setKind(event.target.value)
          },
            h('option', { value: 'all' }, t('allTypes')),
            KIND_FILTERS.map(([value, label]) => h('option', { key: value, value }, label))),
          h('button', { className: 'ue-icon-btn', title: t('reload'), onClick: () => setTick((value) => value + 1) },
            h('svg', { viewBox: '0 0 24 24', width: 13, height: 13, fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' },
              h('path', { d: 'M20 11a8 8 0 1 0-2.34 5.66' }), h('path', { d: 'M20 5v6h-6' })))
        ),
        loading ? h('div', { className: 'ue-progress' }, h('i')) : null,
        error ? h('div', { className: 'ue-banner' }, error) : null,
        h('div', { className: 'ue-scroll' + (staleResults ? ' ue-stale' : '') },
          kind !== 'all' && !query.trim()
            ? h('div', { className: 'ue-empty' }, h(EmptyGlyph), h('div', null, t('filterHint')))
            : results !== null
              ? (rows.length === 0 && !loading
                  ? h('div', { className: 'ue-empty' }, h(EmptyGlyph), h('div', null, t('noResults')),
                      h('button', { className: 'ue-btn', onClick: () => { setQuery(''); setKind('all') } }, t('clearSearch')))
                  : rows.map((node) => {
                      const slash = node.path.lastIndexOf('/');
                      const parent = slash > 0 ? node.path.slice(0, slash) : '';
                      return h(TreeRow, {
                        key: node.path,
                        node: { ...node, subtitle: parent },
                        depth: 0, isOpen: false, selected, onSelect: select, onOpen: open, onToggle: () => {}
                      });
                    }))
              : (root.length === 0 && !loading
                  ? h('div', { className: 'ue-empty' }, h(EmptyGlyph), h('div', null, t('treeEmpty')))
                  : h(Tree, {
                      nodes: root,
                      depth: 0,
                      expanded,
                      selected,
                      onSelect: select,
                      onOpen: open,
                      onToggle: toggle,
                      childrenOf
                    }))
        ),
        results !== null
          ? h('div', { className: 'ue-count' },
              h('span', null, (staleResults ? t('searching') : (resultTotal + ' ' + t('countUnit'))) + (truncated && !staleResults ? ' · 仅显示前 ' + (results ? results.length : 0) : '')),
              h('span', null, kind === 'all' ? '' : (KIND_FILTERS.find(([value]) => value === kind) || [])[1]))
          : null,
        selected ? h('div', { className: 'ue-footer' },
          h('div', { className: 'ue-footer-main', title: selected },
            h('div', { className: 'ue-footer-name' }, selected.slice(selected.lastIndexOf('/') + 1)),
            h('div', { className: 'ue-footer-path' }, selected)),
          h('button', {
            className: 'ue-icon-btn',
            title: t('copyPath'),
            onClick: () => {
              if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(selected).then(() => pushToast(t('copied'))).catch(() => {})
              }
            }
          },
            h('svg', { viewBox: '0 0 24 24', width: 13, height: 13, fill: 'none', stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round', strokeLinejoin: 'round' },
              h('rect', { x: 9, y: 9, width: 11, height: 11, rx: 2 }), h('path', { d: 'M5 15V6a2 2 0 0 1 2-2h9' }))),
          h('button', { className: 'ue-btn', title: t('close'), onClick: () => setSelected(null) }, '×'),
          h('button', { className: 'ue-btn primary', onClick: () => onOpenAsset(selected) }, t('preview'))
        ) : null
      );
    }

    // ── Preview ─────────────────────────────────────────────────────────────

    let vendorPromise = null
    function loadScript(src) {
      return new Promise((resolve, reject) => {
        const script = document.createElement('script')
        script.src = src
        script.async = false
        script.onload = () => resolve()
        script.onerror = () => reject(new Error('无法加载 ' + src))
        document.head.appendChild(script)
      })
    }
    function ensureThree() {
      if (typeof window !== 'undefined' && window.THREE && window.THREE.GLTFLoader && window.THREE.OrbitControls && window.THREE.RoomEnvironment) {
        return Promise.resolve(window.THREE)
      }
      if (!vendorPromise) {
        vendorPromise = loadScript('/ue-asset/vendor/three.min.js')
          .then(() => loadScript('/ue-asset/vendor/RoomEnvironment.js'))
          .then(() => loadScript('/ue-asset/vendor/GLTFLoader.js'))
          .then(() => loadScript('/ue-asset/vendor/OrbitControls.js'))
          .then(() => window.THREE)
          .catch((error) => { vendorPromise = null; throw error })
      }
      return vendorPromise
    }

    function MeshViewer(props) {
      const { url, animationUrl, materials: materialBindings, materialsOmitted } = props
      const hostRef = useRef(null)
      const stateRef = useRef(null)
      const [status, setStatus] = useState('loading')
      const [error, setError] = useState(null)
      const [animations, setAnimations] = useState([])
      const [playing, setPlaying] = useState(false)
      const [wireframe, setWireframe] = useState(false)
      const [textured, setTextured] = useState(true)
      const [textureStats, setTextureStats] = useState(null)
      const [clipDuration, setClipDuration] = useState(0)
      const [clipTime, setClipTime] = useState(0)
      const [speed, setSpeed] = useState(1)

      useEffect(() => {
        let cancelled = false
        let frame = 0
        let teardown = () => {}
        setStatus('loading'); setError(null); setAnimations([]); setPlaying(false)
        const host = hostRef.current
        if (!host) return () => {}

        ensureThree().then((THREE) => {
          if (cancelled || !hostRef.current) return
          const canvas = document.createElement('canvas')
          host.appendChild(canvas)

          const renderer = new THREE.WebGLRenderer({ canvas: canvas, antialias: true, alpha: true })
          renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2))
          renderer.outputEncoding = THREE.sRGBEncoding
          renderer.toneMapping = THREE.ACESFilmicToneMapping
          renderer.toneMappingExposure = 1.0
          renderer.setClearColor(0x000000, 0)

          const scene = new THREE.Scene()
          const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 100000)
          const controls = new THREE.OrbitControls(camera, canvas)
          controls.enableDamping = true
          controls.dampingFactor = 0.08

          scene.add(new THREE.HemisphereLight(0xffffff, 0x444455, 0.8))
          const keyLight = new THREE.DirectionalLight(0xffffff, 1.0)
          keyLight.position.set(1, 2, 1.5)
          scene.add(keyLight)
          const fillLight = new THREE.DirectionalLight(0xffffff, 0.3)
          fillLight.position.set(-1.5, -0.5, -1)
          scene.add(fillLight)

          // UE materials are often metallic; without an environment they render black.
          const pmrem = new THREE.PMREMGenerator(renderer)
          scene.environment = pmrem.fromScene(new THREE.RoomEnvironment(), 0.04).texture

          let mixer = null
          let lastProgress = 0
          const animationsRef = { current: [] }
          const clock = new THREE.Clock()

          const resize = () => {
            const width = hostRef.current ? hostRef.current.clientWidth : 0
            const height = hostRef.current ? hostRef.current.clientHeight : 0
            if (!width || !height) return
            renderer.setSize(width, height, false)
            camera.aspect = width / height
            camera.updateProjectionMatrix()
          }
          const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(resize) : null
          if (observer) observer.observe(host)
          resize()

          const loop = () => {
            frame = requestAnimationFrame(loop)
            // Clamp the first delta after loading (and any long stall): a multi-second step
            // would jump the clip to its end and wrap, which looks like flicker.
            const delta = Math.min(clock.getDelta(), 0.1)
            if (mixer && stateRef.current && stateRef.current.playing) mixer.update(delta * (stateRef.current.speed || 1))
            if (stateRef.current && stateRef.current.action) {
              const now = performance.now()
              if (now - lastProgress > 100) { lastProgress = now; setClipTime(stateRef.current.action.time) }
            }
            controls.update()
            renderer.render(scene, camera)
          }

          const applyWireframe = (enabled) => {
            scene.traverse((object) => {
              if (!object.isMesh || !object.material) return
              const materials = Array.isArray(object.material) ? object.material : [object.material]
              for (const material of materials) material.wireframe = enabled
            })
          }

          stateRef.current = {
            mixer: null,
            playing: false,
            select: null,
            setWireframe: applyWireframe,
            setTextured: (enabled) => {
              scene.traverse((object) => {
                if (!object.isMesh || !object.material) return
                const list = Array.isArray(object.material) ? object.material : [object.material]
                for (const material of list) {
                  if (!material.userData || !material.userData.ueMap) continue
                  material.map = enabled ? material.userData.ueMap : null
                  material.needsUpdate = true
                }
              })
            }
          }

          teardown = () => {
            cancelAnimationFrame(frame)
            if (observer) observer.disconnect()
            controls.dispose()
            scene.traverse((object) => {
              if (object.geometry) object.geometry.dispose()
              const materials = object.material ? (Array.isArray(object.material) ? object.material : [object.material]) : []
              for (const material of materials) {
                for (const value of Object.values(material)) {
                  if (value && value.isTexture) value.dispose()
                }
                if (material.dispose) material.dispose()
              }
            })
            pmrem.dispose()
            renderer.dispose()
            if (canvas.parentNode) canvas.parentNode.removeChild(canvas)
          }

          const computeBounds = (root) => {
            const box = new THREE.Box3()
            const vertex = new THREE.Vector3()
            let sampled = 0
            root.updateMatrixWorld(true)
            root.traverse((object) => {
              if (!object.isMesh || !object.geometry) return
              const position = object.geometry.attributes && object.geometry.attributes.position
              if (!position) return
              const skinned = object.isSkinnedMesh && typeof object.boneTransform === 'function'
              const step = Math.max(1, Math.floor(position.count / 2500))
              for (let index = 0; index < position.count; index += step) {
                if (skinned) {
                  object.boneTransform(index, vertex)
                  vertex.applyMatrix4(object.matrixWorld)
                } else {
                  vertex.fromBufferAttribute(position, index).applyMatrix4(object.matrixWorld)
                }
                box.expandByPoint(vertex)
              }
              sampled++
            })
            if (sampled === 0 || box.isEmpty()) box.setFromObject(root)
            else box.expandByScalar(box.getSize(new THREE.Vector3()).length() * 0.02)
            return box
          }

          // Some UE exports carry morph targets whose accessors are null; three r128's
          // SkinnedMesh.updateMorphTargets then throws on null.name. Drop null targets.
          if (!THREE.SkinnedMesh.prototype.__ueMorphPatched) {
            const originalUpdateMorphTargets = THREE.SkinnedMesh.prototype.updateMorphTargets
            THREE.SkinnedMesh.prototype.updateMorphTargets = function () {
              const attributes = this.geometry && this.geometry.morphAttributes
              if (attributes) {
                for (const key of Object.keys(attributes)) attributes[key] = attributes[key].filter(Boolean)
              }
              return originalUpdateMorphTargets.call(this)
            }
            THREE.SkinnedMesh.prototype.__ueMorphPatched = true
          }

          const loader = new THREE.GLTFLoader()
          loader.load(url, (gltf) => {
            if (cancelled) return
            const root = gltf.scene
            scene.add(root)

            // CUE4Parse writes material JSON + PNGs beside the GLB but does not always link
            // them into the GLB. Attach the diffuse map by material name when we have one.
            let appliedTextures = 0
            let failedTextures = 0
            let materialCount = 0
            if (Array.isArray(materialBindings) && materialBindings.length > 0) {
              const byName = new Map()
              for (const binding of materialBindings) {
                if (binding && binding.material && binding.baseColor) {
                  byName.set(String(binding.material).toLowerCase(), binding.baseColor)
                }
              }
              const textureLoader = new THREE.TextureLoader()
              root.traverse((object) => {
                if (!object.isMesh || !object.material) return
                const list = Array.isArray(object.material) ? object.material : [object.material]
                for (const material of list) {
                  materialCount++
                  const relative = byName.get(String(material.name || '').toLowerCase())
                  if (relative) {
                    const url = '/ue-asset/cache/' + relative.split('/').map(encodeURIComponent).join('/')
                    const texture = textureLoader.load(url, () => { appliedTextures++; setTextureStats({ applied: appliedTextures, failed: failedTextures, total: materialCount }) }, undefined, () => { failedTextures++; setTextureStats({ applied: appliedTextures, failed: failedTextures, total: materialCount }) })
                    texture.encoding = THREE.sRGBEncoding
                    texture.flipY = false
                    // UE UVs routinely leave [0,1]; the default clamp would smear the atlas
                    // border (often blank) across the whole mesh and render it flat white.
                    texture.wrapS = THREE.RepeatWrapping
                    texture.wrapT = THREE.RepeatWrapping
                    material.userData.ueMap = texture
                    material.map = texture
                    // The exporter writes COLOR_0 and three multiplies it into the map; these
                    // meshes carry dark/zero vertex colors, which turned every textured
                    // surface black. Preview uses the albedo map only.
                    material.vertexColors = false
                    if (material.color) material.color.set(0xffffff)
                  }
                  if (material.isMeshStandardMaterial) {
                    // glTF defaults metallicFactor to 1 when omitted; without a real env that
                    // renders black. Treat previewed UE materials as rough dielectrics.
                    material.metalness = 0
                    material.roughness = 0.85
                  }
                  material.needsUpdate = true
                }
              })
            }
            setTextureStats({ applied: appliedTextures, failed: failedTextures, total: materialCount })

            const installClip = (clip) => {
              if (!clip || !clip.tracks || clip.tracks.length === 0) return
              mixer = new THREE.AnimationMixer(root)
              mixer.stopAllAction()
              const player = mixer.clipAction(clip)
              player.reset()
              player.setLoop(THREE.LoopRepeat, Infinity)
              player.play()
              mixer.update(0)
              animationsRef.current = [clip]
              stateRef.current.mixer = mixer
              stateRef.current.playing = true
              stateRef.current.action = player
              stateRef.current.seek = (time) => { player.time = Math.max(0, Math.min(time, clip.duration)); mixer.update(0) }
              setPlaying(true)
              setClipTime(0)
              setClipDuration(clip.duration || 0)
              setAnimations([{ name: clip.name || '动画', index: 0 }])
            }

            const buildClip = (data) => {
              const times = new Float32Array(data.times || [])
              const tracks = []
              for (const track of data.tracks || []) {
                const bone = THREE.PropertyBinding.sanitizeNodeName(track.bone)
                // A track whose bone is missing from this mesh binds to the mixer root in
                // three.js and animates the whole scene (weapon bones did exactly that).
                if (!root.getObjectByName(bone)) continue
                tracks.push(new THREE.QuaternionKeyframeTrack(bone + '.quaternion', times, new Float32Array(track.r)))
                // Root motion moves the cinematic character away from the preview pivot;
                // keep rotations but pin the root/pelvis translation to the rest pose.
                if (track.parent > 0) tracks.push(new THREE.VectorKeyframeTrack(bone + '.position', times, new Float32Array(track.p)))
                tracks.push(new THREE.VectorKeyframeTrack(bone + '.scale', times, new Float32Array(track.s)))
              }
              const last = times.length > 0 ? times[times.length - 1] : 0
              return new THREE.AnimationClip(data.name || 'animation', data.duration || last, tracks)
            }

            const box = computeBounds(root)
            const size = box.getSize(new THREE.Vector3())
            const center = box.getCenter(new THREE.Vector3())
            const radius = Math.max(size.x, size.y, size.z) * 0.5 || 1

            const bounds = new THREE.Box3Helper(box, 0x4f8cff)
            bounds.material.opacity = 0.35
            bounds.material.transparent = true
            scene.add(bounds)

            const distance = radius / Math.tan((camera.fov * Math.PI / 180) / 2) * 1.25
            camera.position.set(center.x + distance * 0.75, center.y + distance * 0.35, center.z + distance * 0.95)
            camera.near = Math.max(radius / 1000, 0.001)
            camera.far = Math.max(radius * 100, 100)
            camera.updateProjectionMatrix()
            controls.target.copy(center)
            controls.update()

            const grid = new THREE.GridHelper(radius * 4, 20, 0x666666, 0x444444)
            grid.position.set(center.x, box.min.y - radius * 0.02, center.z)
            grid.material.opacity = 0.25
            grid.material.transparent = true
            scene.add(grid)

            animationsRef.current = gltf.animations || []
            if (animationsRef.current.length > 0) {
              mixer = new THREE.AnimationMixer(root)
              mixer.clipAction(animationsRef.current[0]).play()
              stateRef.current.mixer = mixer
              stateRef.current.playing = true
              stateRef.current.select = (index) => {
                const clip = animationsRef.current[index]
                if (!clip) return
                mixer.stopAllAction()
                mixer.clipAction(clip).reset().play()
                setPlaying(true)
              }
              setPlaying(true)
            }
            if (animationsRef.current.length === 0 && animationUrl) {
              fetch(animationUrl)
                .then((response) => response.json())
                .then((payload) => { if (!cancelled) installClip(buildClip(payload)) })
                .catch((error) => console.error('[ue-asset] animation load failed', error))
            }
            setAnimations(animationsRef.current.map((clip, index) => ({ name: clip.name || ('动画 ' + (index + 1)), index: index })))
            api('/preview-ack', { url, status: 'loaded' }).catch(() => {})
            setStatus('ready')
          }, undefined, (loadError) => {
            if (cancelled) return
            const message = loadError && loadError.message ? loadError.message : '模型加载失败'
            setError(message)
            api('/preview-ack', { url, status: 'error', error: message }).catch(() => {})
            setStatus('error')
          })

          loop()
        }).catch((loadError) => {
          if (cancelled) return
          setError(loadError.message)
          setStatus('error')
        })

        return () => {
          cancelled = true
          teardown()
          stateRef.current = null
        }
      }, [url])

      useEffect(() => {
        if (stateRef.current) stateRef.current.playing = playing
      }, [playing])

      useEffect(() => {
        if (stateRef.current) stateRef.current.speed = speed
      }, [speed])

      useEffect(() => {
        if (stateRef.current && stateRef.current.setWireframe) stateRef.current.setWireframe(wireframe)
      }, [wireframe])

      useEffect(() => {
        if (stateRef.current && stateRef.current.setTextured) stateRef.current.setTextured(textured)
      }, [textured])

      return h('div', null,
        h('div', { className: 'ue-viewport', ref: hostRef },
          status === 'loading' ? h('div', { className: 'ue-empty', style: { position: 'absolute', inset: 0 } }, t('loading')) : null,
          status === 'error' ? h('div', { className: 'ue-banner', style: { position: 'absolute', inset: 0 } }, error) : null,
          h('div', { className: 'ue-viewport-hint' }, '拖动旋转 · 滚轮缩放'),
          status === 'ready' ? h('div', { className: 'ue-viewport-badge' }, 'GLB' + (textureStats && textureStats.total > 0 ? ' · 贴图 ' + textureStats.applied + '/' + textureStats.total + (textureStats.failed ? ' 失败 ' + textureStats.failed : '') : '')) : null
        ),
        h('div', { className: 'ue-anim' },
          h('button', { className: 'ue-btn', onClick: () => setWireframe((value) => !value) }, wireframe ? '实体' : '线框'),
          h('button', { className: 'ue-btn', onClick: () => setTextured((value) => !value) }, textured ? '贴图' : '无贴图')
        ),
        animations.length > 0
          ? h('div', { className: 'ue-anim' },
              h('select', { className: 'ue-select', onChange: (event) => {
                if (stateRef.current && stateRef.current.select) stateRef.current.select(Number(event.target.value))
              } }, animations.map((clip) => h('option', { key: clip.index, value: clip.index }, clip.name))),
              h('button', { className: 'ue-btn', onClick: () => setPlaying((value) => !value) }, playing ? '⏸' : '▶'),
              clipDuration > 0
                ? h('input', { type: 'range', min: 0, max: clipDuration, step: 0.05, value: clipTime, style: { flex: 1, minWidth: 80 }, onChange: (event) => { const value = Number(event.target.value); setClipTime(value); if (stateRef.current && stateRef.current.seek) stateRef.current.seek(value) } })
                : null,
              h('select', { className: 'ue-select', value: String(speed), onChange: (event) => setSpeed(Number(event.target.value)) },
                [0.5, 1, 1.5, 2].map((value) => h('option', { key: value, value }, value + '×')))
            )
          : null
      )
    }

    function AnimationPicker(props) {
      const { sessionId, initialPath, onPick, onClose } = props
      const [path, setPath] = useState(initialPath || '')
      const [items, setItems] = useState([])
      const [query, setQuery] = useState('')
      const [loading, setLoading] = useState(false)
      const [error, setError] = useState(null)

      const load = async (target) => {
        setLoading(true); setError(null)
        try {
          const page = await api('/tree', { sessionId, path: target, limit: 500 })
          setItems(page.items || []); setPath(target)
        } catch (e) { setError(e.message) }
        finally { setLoading(false) }
      }

      useEffect(() => { load(initialPath || '') }, [initialPath])

      const search = async () => {
        if (!query.trim()) return
        setLoading(true); setError(null)
        try {
          const page = await api('/find', { sessionId, query: query.trim(), kind: 'anim_sequence', nameOnly: true, prefix: path, limit: 100 })
          setItems((page.items || []).map((item) => ({ ...item, isDirectory: false })))
          if ((page.items || []).length === 0) setError('当前目录下没有匹配的动画')
        } catch (e) { setError(e.message) }
        finally { setLoading(false) }
      }

      const up = () => load(path.split('/').slice(0, -1).join('/'))

      return h('div', { className: 'ue-modal-backdrop', onClick: onClose },
        h('div', { className: 'ue-modal', onClick: (event) => event.stopPropagation() },
          h('div', { className: 'ue-modal-head' },
            h('span', null, '选择 AnimSequence'),
            h('button', { className: 'ue-btn', onClick: onClose }, '关闭')),
          h('div', { className: 'ue-anim' },
            h('button', { className: 'ue-btn', onClick: up }, '上一级'),
            h('input', { value: path, onChange: (event) => setPath(event.target.value), onKeyDown: (event) => { if (event.key === 'Enter') load(path) } }),
            h('button', { className: 'ue-btn', onClick: () => load(path) }, '转到')),
          h('div', { className: 'ue-anim' },
            h('input', { value: query, placeholder: '搜索动画名（当前目录优先）', onChange: (event) => setQuery(event.target.value), onKeyDown: (event) => { if (event.key === 'Enter') search() } }),
            h('button', { className: 'ue-btn', onClick: search }, '搜索')),
          error ? h('div', { className: 'ue-banner' }, error) : null,
          loading ? h('div', { className: 'ue-hint' }, '加载中…') : null,
          h('div', { className: 'ue-modal-list' },
            items.filter((item) => item.isDirectory || item.kind === 'anim_sequence').map((item) => h('div', {
              key: item.path,
              className: 'ue-row',
              onClick: () => { if (item.isDirectory) load(item.path); else onPick(item.path) }
            },
              h(TypeIcon, { kind: item.isDirectory ? 'folder' : 'anim_sequence' }),
              h('span', { className: 'ue-name' }, item.name),
              h('span', { className: 'ue-meta' }, item.isDirectory ? '(' + (item.count || 0) + ')' : '')
            )))
        ))
    }

    function PreviewTab(props) {
      const { sessionId, path, pushToast } = props;
      const [data, setData] = useState(null);
      const [error, setError] = useState(null);
      const [loading, setLoading] = useState(false);
      const [animationPath, setAnimationPath] = useState('');
      const [animationChoices, setAnimationChoices] = useState([]);
      const [animationHint, setAnimationHint] = useState(null);
      const [animationOptions, setAnimationOptions] = useState([]);
      const [pickerOpen, setPickerOpen] = useState(false);
      const [elapsed, setElapsed] = useState(0);

      const resolveAnimation = async (raw) => {
        const value = String(raw || '').trim();
        if (!value) return null;
        if (value.endsWith('/')) {
          const found = await api('/find', { sessionId, query: 'AS_', prefix: value, nameOnly: true, kind: 'anim_sequence', limit: 50 });
          const items = (found && found.items) || [];
          if (items.length === 0) throw new Error('该目录下没有 AnimSequence（.uasset）');
          if (items.length === 1) return items[0].path;
          setAnimationChoices(items);
          setAnimationHint('该目录下有 ' + items.length + ' 个动画，请在下拉框选择具体文件');
          return null;
        }
        if (!/\.uasset$/i.test(value)) throw new Error('请输入完整文件路径，以 .uasset 结尾');
        return value;
      };

      const runPreview = useCallback(async (animPath) => {
        setLoading(true); setError(null); setAnimationHint(null);
        try {
          const resolved = await resolveAnimation(animPath);
          const body = { sessionId, path, maxSize: 1024 };
          if (resolved) body.animationPath = resolved;
          const result = await api('/preview', body);
          setData(result);
        } catch (e) { setError((e && e.message) || String(e)); }
        finally { setLoading(false); }
      }, [sessionId, path]);

      // A cold mesh export takes a second or two; show how long the user has been waiting
      // instead of an anonymous pulsing bar.
      useEffect(() => {
        if (!loading) { setElapsed(0); return undefined }
        const started = Date.now();
        const timer = setInterval(() => setElapsed(Math.round((Date.now() - started) / 1000)), 1000);
        return () => clearInterval(timer);
      }, [loading]);

      useEffect(() => {
        setData(null); setError(null); setLoading(false); setAnimationPath('');
        setAnimationChoices([]); setAnimationOptions([]); setAnimationHint(null); setPickerOpen(false);
        if (path) runPreview('');
      }, [runPreview, path]);

      const copyPath = async () => {
        try { await navigator.clipboard.writeText(path || ''); pushToast(t('copied')); } catch {}
      };

      useEffect(() => {
        if (!path || !data || data.kind !== 'mesh') { setAnimationOptions([]); return; }
        let cancelled = false;
        api('/animations', { sessionId, mesh: path })
          .then((list) => { if (!cancelled) setAnimationOptions(Array.isArray(list) ? list : []); })
          .catch(() => { if (!cancelled) setAnimationOptions([]); });
        return () => { cancelled = true; };
      }, [sessionId, path, data && data.kind]);

      const exportOne = async () => {
        try {
          const job = await api('/export', { sessionId, paths: [path] });
          pushToast(t('queued') + ': ' + job.jobId);
        } catch (e) { pushToast(e.message); }
      };

      const image = data && data.mime && data.base64
        ? h('img', { src: 'data:' + data.mime + ';base64,' + data.base64, alt: data.objectPath })
        : null;
      const meta = data && (data.meta || {});
      const mesh = data && data.kind === 'mesh' && meta.modelUrl ? meta : null;
      const json = data && !image && !mesh ? JSON.stringify(meta, null, 2) : null;
      const fullName = (data && data.objectPath) || path || '';
      const shortName = fullName ? fullName.slice(fullName.lastIndexOf('/') + 1) : '';
      const body = mesh
        ? h('div', null,
            h(MeshViewer, { key: mesh.modelUrl + (mesh.animation?.url || ''), url: mesh.modelUrl, animationUrl: mesh.animation && mesh.animation.url, materials: mesh.materialBindings || [], materialsOmitted: mesh.materials === false }),
            h('div', { className: 'ue-meta' }, 'GLB ' + fmtSize(mesh.bytes) + (mesh.animation && mesh.animation.clip ? ' · 动画 ' + mesh.animation.clip.name : (mesh.animations ? ' · 含动画' : ' · 静态')) + (mesh.materials === false ? ' · 材质未导出' : (Array.isArray(mesh.materialBindings) && mesh.materialBindings.length > 0 ? ' · 贴图 ' + mesh.materialBindings.length : '')))
          )
        : h('div', { className: 'ue-canvas' }, image || h('span', { className: 'ue-meta' }, data ? data.className || '' : ''));

      return h('div', { className: 'ue-pp' },
        h('div', { className: 'ue-pp-head' },
          path ? h(TypeIcon, { kind: (data && data.kind) || 'asset' }) : null,
          h('div', { className: 'ue-pp-info' },
            h('div', { className: 'ue-pp-title', title: fullName }, shortName || t('preview')),
            h('div', { className: 'ue-pp-path', title: path || '' }, path || '')),
          path ? h('button', { className: 'ue-icon-btn', title: t('reload'), disabled: loading, onClick: () => runPreview(animationPath) }, h(IconRefresh)) : null,
          path ? h('button', { className: 'ue-icon-btn', title: t('copyPath'), onClick: copyPath }, h(IconCopy)) : null,
          path ? h('button', { className: 'ue-btn primary', disabled: loading, onClick: exportOne }, t('export')) : null
        ),
        loading ? h('div', { className: 'ue-progress' }, h('i')) : null,
        error ? h('div', { className: 'ue-banner' }, t('preview') + '失败：' + error,
          h('button', { className: 'ue-btn', style: { marginLeft: 8 }, onClick: () => runPreview(animationPath) }, t('retry'))) : null,
        !path
          ? h('div', { className: 'ue-empty' }, h(EmptyGlyph), h('div', null, t('noPreview')))
          : (loading && !data
              ? h('div', { className: 'ue-skel' },
                  h('i', { style: { width: '66%' } }), h('i', { style: { width: '92%' } }), h('i', { style: { width: '48%' } }),
                  h('div', { className: 'ue-skel-label' }, t('preparing') + (elapsed > 0 ? ' ' + elapsed + 's' : '')))
              : (!data
                  ? h('div', { className: 'ue-empty' }, h(EmptyGlyph), h('div', null, error ? '预览失败' : t('noPreview')))
                  : h('div', { className: 'ue-scroll' },
                  h('div', { className: 'ue-preview' },
                    data && data.kind === 'mesh'
                      ? h(React.Fragment, null,
                          animationOptions.length > 0
                            ? h('select', { className: 'ue-select', style: { maxWidth: '100%', width: '100%' }, value: '', onChange: (event) => { const value = event.target.value; if (value) { setAnimationPath(value); runPreview(value); } } },
                                h('option', { value: '' }, '选择该网格的动画（' + animationOptions.length + '）'),
                                animationOptions.map((item) => h('option', { key: item.path, value: item.path }, item.name)))
                            : null,
                          h('div', { className: 'ue-anim' },
                            h('input', { value: animationPath, placeholder: '动画路径（.uasset）或目录（末尾 /）', onChange: (event) => setAnimationPath(event.target.value), onKeyDown: (event) => { if (event.key === 'Enter') runPreview(animationPath); } }),
                            h('button', { className: 'ue-btn', onClick: () => runPreview(animationPath) }, '加载'),
                            h('button', { className: 'ue-btn', onClick: () => setPickerOpen(true) }, '目录'))
                        )
                      : null,
                    animationHint ? h('div', { className: 'ue-hint' }, animationHint) : null,
                    animationChoices.length > 0
                      ? h('select', { className: 'ue-select', style: { maxWidth: '100%', width: '100%' }, onChange: (event) => { const value = event.target.value; if (value) { setAnimationPath(value); runPreview(value); } } },
                          h('option', { value: '' }, '选择该目录下的动画…'),
                          animationChoices.map((item) => h('option', { key: item.path, value: item.path }, item.name)))
                      : null,
                    pickerOpen
                      ? h(AnimationPicker, {
                          sessionId,
                          initialPath: (path || '').split('/').slice(0, -1).join('/'),
                          onPick: (selected) => { setPickerOpen(false); setAnimationPath(selected); runPreview(selected); },
                          onClose: () => setPickerOpen(false)
                        })
                      : null,
                    body,
                    h('div', { className: 'ue-pp-meta' },
                      h('span', { className: 'ue-chip' }, '[' + ((data && data.className) || '?') + ']'),
                      data && data.kind ? h('span', { className: 'ue-chip' }, data.kind) : null,
                      data && data.width ? h('span', { className: 'ue-chip' }, data.width + '×' + data.height) : null),
                    data && data.note ? h('div', { className: 'ue-hint' }, data.note) : null,
                    mesh && mesh.animationError ? h('div', { className: 'ue-banner' }, mesh.animationError) : null,
                    json && json.length > 2
                      ? h('details', { className: 'ue-props' },
                          h('summary', null,
                            h('span', null, '属性 JSON'),
                            h('span', { className: 'ue-fold-count' }, json.length + ' 字符')),
                          h('pre', { className: 'ue-pre' }, json))
                      : null
                  )))
        )
      );
    }


    function UePanel() {
      const [tab, setTab] = useState('browse')
      const [sessions, setSessions] = useState([])
      const [sessionId, setSessionId] = useState(null)
      const [config, setConfig] = useState(null)
      const [previewPath, setPreviewPath] = useState(null)
      const [error, setError] = useState(null)
      const [opening, setOpening] = useState(false)
      const [pakCatalog, setPakCatalog] = useState(null)
      const [selectedPaks, setSelectedPaks] = useState([])
      // Per-pak AES state (pakKeys) and the one inline key editor that is currently open.
      const [pakKeys, setPakKeys] = useState({})
      const [keyEditor, setKeyEditor] = useState(null)
      const [keyBusy, setKeyBusy] = useState(null)
      const [activeProfile, setActiveProfile] = useState(null)
      const [pakQuery, setPakQuery] = useState('')
      const [icons, setIcons] = useState({})
      const [closing, setClosing] = useState(null)
      const previewPaneRef = useRef(null)
      const { toast, push } = useToasts()

      const refresh = useCallback(async () => {
        try {
          const [cfg, list] = await Promise.all([api('/config'), api('/sessions')])
          setConfig(cfg)
          const sessionsList = (list && list.sessions) || (Array.isArray(list) ? list : [])
          setSessions(sessionsList)
          setSessionId((current) => (current && sessionsList.some((s) => s.sessionId === current) ? current : null))
          setError(null)
        } catch (e) { setError(e.message) }
      }, [])

      useEffect(() => { refresh() }, [refresh])
      useEffect(() => {
        const timer = setInterval(() => { if (!sessionId) refresh() }, 5000)
        return () => clearInterval(timer)
      }, [sessionId, refresh])

      useEffect(() => {
        const timer = setInterval(async () => {
          try {
            const response = await api('/pending-open', {})
            const item = response && response.item
            if (!item || !item.sessionId || !item.path) return
            await refresh()
            setSessionId(item.sessionId)
            setPreviewPath(item.path)
            setTab('preview')
          } catch {}
        }, 1500)
        return () => clearInterval(timer)
      }, [refresh])

      const profileNames = config && config.profiles ? config.profiles.map((profile) => profile.name).join('|') : ''
      useEffect(() => {
        if (!config) return
        let cancelled = false
        Promise.all((config.profiles || []).map(async (profile) => {
          try {
            const result = await api('/profile-icon', { profile: profile.name })
            return [profile.name, result && result.icon ? 'data:image/png;base64,' + result.icon : null]
          } catch { return [profile.name, null] }
        })).then((entries) => { if (!cancelled) setIcons(Object.fromEntries(entries)) })
        return () => { cancelled = true }
      }, [profileNames])

      useEffect(() => {
        if (previewPaneRef.current) previewPaneRef.current.scrollTop = 0
      }, [previewPath, sessionId, tab])

      const loadPaks = async (profile) => {
        setActiveProfile(profile); setError(null); setPakQuery(''); setKeyEditor(null); setPakKeys({})
        try {
          // Key state is advisory: an older Host without /pak-keys must not break pak selection.
          const [catalog, keys] = await Promise.all([
            api('/paks', { profile }),
            api('/pak-keys', { profile }).catch(() => [])
          ])
          setPakCatalog(catalog)
          setPakKeys(Object.fromEntries((keys || []).map((item) => [item.pak, item])))
          const defaults = catalog.defaults && catalog.defaults.length > 0
            ? catalog.defaults
            : catalog.available.filter((pak) => pak.chunk === 0).map((pak) => pak.name)
          setSelectedPaks(defaults)
        } catch (e) { setError(e.message) }
      }

      const closeSession = async (id) => {
        if (!id) return
        setClosing(id); setError(null)
        try {
          await api('/close', { sessionId: id })
          setSessions((prev) => prev.filter((session) => session.sessionId !== id))
          if (sessionId === id) { setSessionId(null); setPreviewPath(null); setTab('browse') }
          await refresh()
          push(t('sessionClosed'))
        } catch (e) { setError(e.message) }
        finally { setClosing(null) }
      }

      const tailDir = (value) => String(value || '').split(/[\\/]/).filter(Boolean).slice(-2).join('/')

      const openProfile = async (profile, paks) => {
        setOpening(true); setError(null)
        try {
          const body = { profile }
          if (Array.isArray(paks) && paks.length > 0) body.paks = paks
          const summary = await api('/open', body)
          await refresh()
          setSessionId(summary.sessionId)
          setTab('browse')
        } catch (e) { setError(e.message) }
        finally { setOpening(false) }
      }

      const togglePak = (name) => setSelectedPaks((prev) => prev.includes(name) ? prev.filter((item) => item !== name) : [...prev, name])

      // One key per pak: the Host validates it against the pak index header before storing it,
      // so a typo is reported here instead of silently producing unreadable archives.
      const bindPakKey = async (pak) => {
        const value = ((keyEditor && keyEditor.value) || '').trim()
        if (!value) return
        setKeyBusy(pak); setError(null)
        try {
          const result = await api('/pak-key', { profile: activeProfile, pak, key: value })
          if (!result || !result.bound) { setError((result && result.reason) || '密钥未被这个 pak 接受'); return }
          setPakKeys((prev) => ({ ...prev, [pak]: { pak, encrypted: true, hasKey: true, keyGuid: result.keyGuid } }))
          setKeyEditor(null)
          if (result.mounted) await refresh()
          push('已保存密钥 ' + (result.keyGuid ? '(' + result.keyGuid + ')' : ''))
        } catch (e) { setError(e.message) }
        finally { setKeyBusy(null) }
      }
      const current = sessions.find((session) => session.sessionId === sessionId) || null

      const profiles = (config && config.profiles) || []
      const defaultsFor = (catalog) => {
        if (!catalog) return []
        const list = catalog.defaults && catalog.defaults.length > 0 ? catalog.defaults : catalog.available.filter((pak) => pak.chunk === 0).map((pak) => pak.name)
        return list.length > 0 ? list : catalog.available.slice(0, 1).map((pak) => pak.name)
      }
      const filteredPaks = pakCatalog
        ? pakCatalog.available.filter((pak) => !pakQuery.trim() || pak.name.toLowerCase().includes(pakQuery.trim().toLowerCase()))
        : []

      const loadBody = pakCatalog
        ? h('div', { className: 'ue-lp' },
            h('div', { className: 'ue-lp-head' },
              h(GameBadge, { src: icons[activeProfile], name: activeProfile }),
              h('div', { className: 'ue-lp-title', title: (activeProfile || '') + (pakCatalog.paksDirectory ? ' · ' + pakCatalog.paksDirectory : '') }, activeProfile + (tailDir(pakCatalog.paksDirectory) ? ' · ' + tailDir(pakCatalog.paksDirectory) : '')),
              h('button', { className: 'ue-btn', onClick: () => { setPakCatalog(null); setActiveProfile(null); setPakQuery('') } }, t('back'))
            ),
            h('div', { className: 'ue-lp-body' },
              h('input', { className: 'ue-pak-filter', value: pakQuery, placeholder: t('filterPaks'), onChange: (event) => setPakQuery(event.target.value), onKeyDown: (event) => { if (event.key === 'Escape') setPakQuery('') } }),
              filteredPaks.map((pak) => {
                const state = pakKeys[pak.name]
                const encrypted = !!(state && state.encrypted)
                const needsKey = encrypted && !state.hasKey
                const editing = keyEditor && keyEditor.pak === pak.name
                return h('div', { key: pak.name, className: 'ue-pak' },
                  h('label', { className: 'ue-pak-main', title: pak.name + (state && state.keyGuid ? '\nAES ' + state.keyGuid : '') },
                    h('input', { type: 'checkbox', checked: selectedPaks.includes(pak.name), onChange: () => togglePak(pak.name) }),
                    h('span', { className: 'ue-pak-name' }, pak.name.replace(/^pakchunk/, 'chunk').replace(/\.(pak|utoc|ucas)$/i, '')),
                    h('span', { className: 'ue-chip' }, 'c' + pak.chunk),
                    (pak.tags || []).length > 0 ? h('span', { className: 'ue-chip' }, pak.tags.join(',')) : null),
                  needsKey
                    ? h('button', {
                        className: 'ue-pak-lock warn',
                        title: '缺少 AES 密钥（' + (state.keyGuid || '') + '），点击填写',
                        onClick: () => setKeyEditor(editing ? null : { pak: pak.name, value: '' })
                      }, '🔒')
                    : null,
                  editing
                    ? h('div', { className: 'ue-key-row' },
                        h('input', {
                          className: 'ue-key-input',
                          autoFocus: true,
                          placeholder: 'AES key（32 位十六进制）',
                          value: keyEditor.value,
                          onChange: (event) => setKeyEditor({ pak: pak.name, value: event.target.value }),
                          onKeyDown: (event) => {
                            if (event.key === 'Enter') bindPakKey(pak.name)
                            if (event.key === 'Escape') setKeyEditor(null)
                          }
                        }),
                        h('button', { className: 'ue-btn primary', disabled: keyBusy === pak.name, onClick: () => bindPakKey(pak.name) }, keyBusy === pak.name ? '…' : '保存'))
                    : null)
              }),
              filteredPaks.length === 0 ? h('div', { className: 'ue-hint' }, t('noResults')) : null
            ),
            h('div', { className: 'ue-lp-foot' },
              h('span', { className: 'ue-grow' }, selectedPaks.length + ' / ' + pakCatalog.available.length),
              h('button', { className: 'ue-btn', onClick: () => setSelectedPaks(pakCatalog.available.map((pak) => pak.name)) }, '全选'),
              h('button', { className: 'ue-btn', onClick: () => setSelectedPaks([]) }, '清空'),
              h('button', { className: 'ue-btn', onClick: () => setSelectedPaks(defaultsFor(pakCatalog)) }, '默认'),
              h('button', { className: 'ue-btn primary', disabled: selectedPaks.length === 0 || opening, onClick: () => openProfile(activeProfile, selectedPaks) }, opening ? t('loading') : '打开 (' + selectedPaks.length + ')')
            )
          )
        : h('div', { className: 'ue-lp' },
            h('div', { className: 'ue-lp-head' },
              h('div', { className: 'ue-lp-title' }, t('chooseGame')),
              h('button', { className: 'ue-icon-btn', title: t('refresh'), onClick: () => refresh() }, h(IconRefresh))),
            h('div', { className: 'ue-lp-body' },
              sessions.length > 0
                ? h(React.Fragment, null,
                    h('div', { className: 'ue-lp-sec' }, '已挂载的会话'),
                    sessions.map((item) => h('div', { key: item.sessionId, className: 'ue-sess' },
                      h(GameBadge, { src: icons[item.profile], name: item.profile }),
                      h('div', { className: 'ue-game-card-body' },
                        h('div', { className: 'ue-game-card-name' }, item.profile || item.sessionId),
                        h('div', { className: 'ue-game-card-sub' }, 'PAK ' + item.mounted + '/' + (item.mounted + item.missing) + (item.needsKey ? ' · 缺少 ' + item.missing + ' 个密钥' : ''))),
                      h('button', { className: 'ue-btn', onClick: () => setSessionId(item.sessionId) }, '继续'),
                      h('button', { className: 'ue-icon-btn', title: t('closeSession'), disabled: closing === item.sessionId, onClick: () => closeSession(item.sessionId) }, h(IconClose)))))
                : null,
              h('div', { className: 'ue-lp-sec' }, '选择游戏'),
              profiles.length === 0
                ? h('div', { className: 'ue-hint' }, t('noProfiles'))
                : h('div', { className: 'ue-games' },
                    profiles.map((profile) => h('div', {
                      key: profile.name,
                      className: 'ue-game-card' + (activeProfile === profile.name ? ' on' : ''),
                      title: (profile.game ? profile.game + '\n' : '') + (profile.directory || ''),
                      onClick: () => loadPaks(profile.name)
                    },
                      h(GameBadge, { src: icons[profile.name], name: profile.name }),
                      h('div', { className: 'ue-game-card-body' },
                        h('div', { className: 'ue-game-card-name' }, profile.name),
                        h('div', { className: 'ue-game-card-sub' }, tailDir(profile.directory) || profile.game || '')))))
            )
          )

      const body = !sessionId
        ? loadBody
        : h(React.Fragment, null,
            current && current.needsKey ? h('div', { className: 'ue-banner' }, t('needsKey') + ' (' + current.missing + ')') : null,
            h('div', { className: 'ue-pane', hidden: tab !== 'browse' },
              h(BrowseTab, { sessionId, pushToast: push, onOpenAsset: (path) => { setPreviewPath(path); setTab('preview') } })),
            h('div', { className: 'ue-pane', ref: previewPaneRef, hidden: tab !== 'preview' },
              h(PreviewTab, { key: (sessionId || '') + '|' + (previewPath || ''), sessionId, path: previewPath, pushToast: push }))
          )

      return h('div', { className: 'ue-root' },
        h('div', { className: 'ue-tabs' },
          h('div', { className: 'ue-tab' + (tab === 'browse' ? ' on' : ''), onClick: () => setTab('browse') }, t('browse')),
          h('div', { className: 'ue-tab' + (tab === 'preview' ? ' on' : ''), onClick: () => setTab('preview') }, t('preview'))
        ),
        h('div', { className: 'ue-toolbar' },
          h('select', {
            className: 'ue-select',
            value: sessionId || '',
            onChange: (event) => setSessionId(event.target.value || null)
          },
            h('option', { value: '' }, sessions.length ? '—' : t('empty')),
            sessions.map((session) => h('option', { key: session.sessionId, value: session.sessionId },
              (session.profile || session.sessionId) + ' · ' + session.mounted + '/' + (session.mounted + session.missing)))
          ),
          current ? h('span', { className: 'ue-status ' + (current.needsKey ? 'warn' : 'ok') }) : null,
          current ? h('button', {
            className: 'ue-icon-btn',
            title: t('closeSession'),
            disabled: closing === current.sessionId,
            onClick: () => closeSession(current.sessionId)
          }, h(IconClose)) : null,
          h('button', { className: 'ue-icon-btn', title: t('refresh'), onClick: () => refresh() }, h(IconRefresh)),
          error ? h('span', { className: 'ue-hint', title: error }, '!') : null
        ),
        body,
        toast ? h('div', { className: 'ue-toast' }, toast) : null
      )
    }

    function GameBadge(props) {
      if (props.src) return h('img', { className: 'ue-game-icon', src: props.src, alt: '' })
      const initial = String(props.name || '?').trim().charAt(0).toUpperCase()
      return h('span', { className: 'ue-game-icon placeholder' }, initial)
    }

    function SettingsSection() {
      const [config, setConfig] = useState(null)
      const [global, setGlobal] = useState(null)
      const [draft, setDraft] = useState(null)
      const [originalName, setOriginalName] = useState(null)
      const [aesKey, setAesKey] = useState('')
      const [error, setError] = useState(null)
      const [pakTagsCatalog, setPakTagsCatalog] = useState(null)
      const [tagDrafts, setTagDrafts] = useState({})
      const [pakKeyStates, setPakKeyStates] = useState([])
      const [keyDrafts, setKeyDrafts] = useState({})
      const [keyBusy, setKeyBusy] = useState(false)
      const [games, setGames] = useState([])
      const [icons, setIcons] = useState({})
      const [oodleBusy, setOodleBusy] = useState(false)
      const { toast, push } = useToasts()

      const loadIcons = async (profiles) => {
        const entries = await Promise.all((profiles || []).map(async (profile) => {
          try {
            const result = await api('/profile-icon', { profile: profile.name })
            return [profile.name, result && result.icon ? 'data:image/png;base64,' + result.icon : null]
          } catch {
            return [profile.name, null]
          }
        }))
        setIcons(Object.fromEntries(entries))
      }

      const load = async () => {
        try {
          const cfg = await api('/config')
          setConfig(cfg)
          loadIcons(cfg.profiles)
          setGlobal({
            outputRoot: cfg.outputRoot || '',
            cacheRoot: cfg.cacheRoot || '',
            oodleLibrary: cfg.oodleLibrary || '',
            defaultMeshFormat: cfg.defaultMeshFormat || 'UEFormat',
            maxSessions: cfg.maxSessions,
            maxJobs: cfg.maxJobs,
            maxBatchAssets: cfg.maxBatchAssets
          })
        } catch (e) { setError(e.message) }
      }

      useEffect(() => {
        load()
        // EGame is reflected from the parser on the Host; an older Host without the route simply
        // leaves the list empty and the field falls back to a free-text input.
        api('/config/games')
          .then((result) => setGames((result && result.games) || []))
          .catch(() => {})
      }, [])

      const loadPakTags = async (profileName) => {
        try {
          const catalog = await api('/paks', { profile: profileName })
          // An unreadable pak directory answers with an error object, so normalize here instead of
          // crashing the whole draft when the tag fold reads catalog.available.
          const available = Array.isArray(catalog && catalog.available) ? catalog.available : []
          setPakTagsCatalog({ ...(catalog || {}), available })
          const drafts = {}
          for (const pak of available) drafts[pak.name] = (pak.tags || []).join(',')
          setTagDrafts(drafts)
        } catch (e) { setError(e.message); setPakTagsCatalog(null) }
      }

      const savePakTags = async () => {
        if (!pakTagsCatalog) return
        try {
          for (const pak of pakTagsCatalog.available) {
            const tags = String(tagDrafts[pak.name] || '').split(',').map((s) => s.trim()).filter(Boolean)
            await api('/tag/set', { profile: pakTagsCatalog.profile, pak: pak.name, tags })
          }
          await loadPakTags(pakTagsCatalog.profile)
          push('PAK 标签已保存')
        } catch (e) { setError(e.message) }
      }

      // Every registered pak is listed here; the panel only renders the ones whose index header
      // the profile main key cannot open, so the usual case is an empty block.
      const loadPakKeys = async (profileName) => {
        setPakKeyStates([])
        setKeyDrafts({})
        try {
          const states = await api('/pak-keys', { profile: profileName })
          setPakKeyStates(Array.isArray(states) ? states : [])
        } catch { setPakKeyStates([]) }
      }

      const bindPakKey = async (pakName) => {
        if (!draft) return
        const key = String(keyDrafts[pakName] || '').trim()
        if (!key) return
        setKeyBusy(true)
        try {
          const result = await api('/pak-key', { profile: draft.name, pak: pakName, key })
          if (result && result.bound) {
            push('已绑定 ' + pakName)
            await loadPakKeys(draft.name)
          } else {
            setError((result && result.reason) || 'key 被拒绝')
          }
        } catch (e) { setError(e.message) } finally { setKeyBusy(false) }
      }

      const clearPakKey = async (pakName) => {
        if (!draft) return
        setKeyBusy(true)
        try {
          await api('/pak-key/clear', { profile: draft.name, pak: pakName })
          push('已清除 ' + pakName + '，重新打开会话生效')
          await loadPakKeys(draft.name)
        } catch (e) { setError(e.message) } finally { setKeyBusy(false) }
      }

      const selectProfile = (profile) => {
        setOriginalName(profile.name)
        loadPakTags(profile.name)
        loadPakKeys(profile.name)
        setAesKey('')
        setDraft({
          name: profile.name,
          directory: profile.directory,
          extraDirectories: (profile.extraDirectories || []).join('\n'),
          game: profile.game,
          platform: profile.platform,
          usmap: profile.usmap || '',
          aesKey: '',
          clearAesKey: false,
          hasAesKey: profile.hasAesKey,
          prefixRules: (profile.prefixRules || []).map((rule) => ({ prefix: rule.prefix, kind: rule.kind }))
        })
      }

      const saveProfile = async () => {
        if (!draft) return
        try {
          await api('/config/profile', {
            originalName,
            name: draft.name,
            directory: draft.directory,
            extraDirectories: draft.extraDirectories.split(/\r?\n/).map((s) => s.trim()).filter(Boolean),
            game: draft.game,
            platform: draft.platform,
            usmap: draft.usmap || null,
            aesKey: aesKey || null,
            clearAesKey: !!draft.clearAesKey,
            prefixRules: draft.prefixRules || []
          })
          setAesKey('')
          push(t('save'))
          await load()
        } catch (e) { setError(e.message) }
      }

      const deleteProfile = async () => {
        if (!originalName) return
        try {
          await api('/config/delete', { name: originalName })
          setDraft(null); setOriginalName(null)
          await load()
        } catch (e) { setError(e.message) }
      }

      const acquireOodle = async () => {
        setOodleBusy(true)
        try {
          const result = await api('/oodle/download', {})
          setGlobal((prev) => (prev ? { ...prev, oodleLibrary: result.path } : prev))
          push(result.method === 'download' ? '已下载 Oodle 到 ' + result.path : '已使用本机 Oodle：' + result.path)
        } catch (e) { setError(e.message) } finally { setOodleBusy(false) }
      }

      const saveGlobal = async () => {
        try {
          const cfg = await api('/config/global', global)
          setConfig(cfg)
          push(t('save'))
        } catch (e) { setError(e.message) }
      }

      const renderPrefixRules = () => h('div', null,
        (draft.prefixRules || []).length > 0
          ? h('div', { className: 'ue-scroll', style: { maxHeight: 160, marginBottom: 6 } },
              (draft.prefixRules || []).map((rule, index) => h('div', { key: index, className: 'ue-anim', style: { display: 'flex', gap: 4, marginBottom: 4 } },
                h('input', { value: rule.prefix, placeholder: '前缀，如 SKM_', onChange: (event) => { const list = [...(draft.prefixRules || [])]; list[index] = { ...rule, prefix: event.target.value }; setDraft({ ...draft, prefixRules: list }) } }),
                h('input', { value: rule.kind, placeholder: 'kind，如 data_asset', onChange: (event) => { const list = [...(draft.prefixRules || [])]; list[index] = { ...rule, kind: event.target.value }; setDraft({ ...draft, prefixRules: list }) } }),
                h('button', { className: 'ue-btn', style: { flex: 'none' }, onClick: () => { const list = [...(draft.prefixRules || [])]; list.splice(index, 1); setDraft({ ...draft, prefixRules: list }) } }, '删除')
              )))
          : h('div', { className: 'ue-hint', style: { marginBottom: 6 } }, 'AssetRegistry 里查不到类型时，才按文件名前缀归类。'),
        h('button', { className: 'ue-btn', onClick: () => setDraft({ ...draft, prefixRules: [...(draft.prefixRules || []), { prefix: '', kind: 'asset' }] }) }, '新增规则')
      )

      // Only paks whose index header the profile main key cannot open are listed, and saving one
      // goes through the same validated bind the PAK picker uses, so a wrong key is rejected here.
      const renderPakKeys = () => {
        const need = pakKeyStates.filter((state) => state.needsOwnKey)
        if (need.length === 0) {
          return h('div', { className: 'ue-hint' },
            pakKeyStates.length > 0
              ? '这个配置里的加密包都能用上面的主密钥解开，无需单独的 key。'
              : t('loading'))
        }
        return h('div', null,
          h('div', { className: 'ue-scroll', style: { maxHeight: 200, marginBottom: 6 } },
            need.map((state) => h('div', { key: state.pak, style: { marginBottom: 6 } },
              h('div', { className: 'ue-meta', style: { display: 'flex', gap: 6, alignItems: 'center', marginBottom: 2 } },
                h('span', { style: { flex: 1, minWidth: 0, overflowWrap: 'anywhere' } }, state.pak),
                state.hasKey
                  ? h('span', { style: { flex: 'none', display: 'flex', gap: 4, alignItems: 'center' } },
                      h('span', null, '已配置'),
                      h('button', { className: 'ue-btn', style: { flex: 'none' }, disabled: keyBusy, onClick: () => clearPakKey(state.pak) }, '清除'))
                  : null),
              h('div', { style: { display: 'flex', gap: 4 } },
                h('input', {
                  type: 'password',
                  value: keyDrafts[state.pak] || '',
                  placeholder: 'AES key（32 位十六进制）',
                  onChange: (event) => setKeyDrafts({ ...keyDrafts, [state.pak]: event.target.value })
                }),
                h('button', {
                  className: 'ue-btn',
                  style: { flex: 'none' },
                  disabled: keyBusy || !String(keyDrafts[state.pak] || '').trim(),
                  onClick: () => bindPakKey(state.pak)
                }, t('save'))))))
        )
      }

      const renderGameSelect = () => {
        if (games.length === 0) {
          return h('input', { value: draft.game, onChange: (event) => setDraft({ ...draft, game: event.target.value }) })
        }
        const groups = []
        for (const game of games) {
          let group = groups.find((item) => item.group === game.group)
          if (!group) { group = { group: game.group, items: [] }; groups.push(group) }
          group.items.push(game)
        }
        const current = draft.game || ''
        const known = games.some((game) => game.name === current)
        return h('select', {
          value: current,
          onChange: (event) => setDraft({ ...draft, game: event.target.value })
        },
          !known && current ? h('option', { key: current, value: current }, current + '  (当前值)') : null,
          groups.map((group) => h('optgroup', {
            key: group.group,
            label: group.group === 'Games' ? t('games') : group.group
          },
            group.items.map((game) => h('option', { key: game.name, value: game.name }, game.label + '  ·  ' + game.name))))
        )
      }

      const renderFold = (title, count, body) => h('details', { className: 'ue-fold' },
        h('summary', null,
          h('span', null, title),
          h('span', { className: 'ue-fold-count' }, String(count))),
        h('div', { className: 'ue-fold-body' }, body)
      )

      const saveAndReopen = async () => {
        await saveProfile()
        if (!draft) return
        try { await api('/open', { profile: draft.name, paks: ['all'] }) } catch {}
      }

      const renderDraft = () => {
        if (!draft) return null
        return h('div', { className: 'ue-game-body' },
          h('div', { className: 'ue-field' }, h('label', null, t('name')), h('input', { value: draft.name, onChange: (event) => setDraft({ ...draft, name: event.target.value }) })),
          h('div', { className: 'ue-field' }, h('label', null, t('directory')), h('input', { value: draft.directory, onChange: (event) => setDraft({ ...draft, directory: event.target.value }) })),
          h('div', { className: 'ue-field' }, h('label', null, t('extra')), h('textarea', { rows: 2, value: draft.extraDirectories, onChange: (event) => setDraft({ ...draft, extraDirectories: event.target.value }) })),
          h('div', { className: 'ue-field' }, h('label', null, t('game')), renderGameSelect()),
          h('div', { className: 'ue-field' }, h('label', null, t('platform')),
            h('select', { value: draft.platform, onChange: (event) => setDraft({ ...draft, platform: event.target.value }) },
              ['DesktopMobile', 'XboxAndPlaystation4', 'NintendoSwitch', 'Playstation5'].map((value) => h('option', { key: value, value }, value)))),
          h('div', { className: 'ue-field' }, h('label', null, t('aes')), h('input', { type: 'password', value: aesKey, placeholder: draft.hasAesKey ? t('aesSet') : '', onChange: (event) => setAesKey(event.target.value) })),
          h('label', { className: 'ue-hint', style: { display: 'flex', gap: 6, alignItems: 'center', marginBottom: 8 } },
            h('input', { type: 'checkbox', checked: !!draft.clearAesKey, onChange: (event) => setDraft({ ...draft, clearAesKey: event.target.checked }) }),
            t('aesClear')),
          h('div', { className: 'ue-field' }, h('label', null, t('usmap')), h('input', { value: draft.usmap, onChange: (event) => setDraft({ ...draft, usmap: event.target.value }) })),
          renderFold('PAK 标签（逗号分隔）', pakTagsCatalog ? pakTagsCatalog.available.length : 0,
            pakTagsCatalog
              ? h('div', null,
                  h('div', { className: 'ue-scroll' },
                    pakTagsCatalog.available.map((pak) => h('div', { key: pak.name, style: { marginBottom: 4 } },
                      h('div', { className: 'ue-meta', style: { marginBottom: 2 } }, pak.name),
                      h('input', { value: tagDrafts[pak.name] || '', placeholder: '如 base,ui,chapter1', onChange: (event) => setTagDrafts({ ...tagDrafts, [pak.name]: event.target.value }) })
                    ))
                  ),
                  h('button', { className: 'ue-btn', onClick: savePakTags, style: { marginTop: 6 } }, '保存标签'))
              : h('div', { className: 'ue-hint' }, t('loading'))),
          renderFold('类型前缀', (draft.prefixRules || []).length, renderPrefixRules()),
          renderFold('PAK 密钥', pakKeyStates.filter((state) => state.needsOwnKey).length, renderPakKeys()),
          h('div', { className: 'ue-actions' },
            h('button', { className: 'ue-btn primary', onClick: saveProfile }, t('save')),
            h('button', { className: 'ue-btn', onClick: saveAndReopen }, '保存并重新打开'),
            originalName ? h('button', { className: 'ue-btn danger', style: { marginLeft: 'auto' }, onClick: deleteProfile }, t('remove')) : null)
        )
      }

      if (!config || !global) return h('div', { className: 'ue-empty' }, t('loading'))

      // 设置页由宿主铺底（bg-layer-2）：自己再画一层 bg-base 会变成一块更暗的矩形。
      return h('div', { className: 'ue-root', style: { background: 'transparent', height: 'auto' } },
        h('div', { className: 'ue-section' },
          h('h4', { style: { display: 'flex', alignItems: 'center' } }, h('span', { className: 'ue-ue-mark', 'aria-hidden': 'true' }), t('settings')),
          h('div', { className: 'ue-hint' }, '配置文件：' + (config.configPath || '')),
          h('div', { style: { display: 'flex', gap: 6, flexWrap: 'wrap', marginTop: 8 } },
            h('button', { className: 'ue-btn', onClick: () => { setOriginalName(null); setAesKey(''); setDraft({ name: '', directory: '', extraDirectories: '', game: 'GAME_UE5_0', platform: 'DesktopMobile', usmap: '', aesKey: '', clearAesKey: false, hasAesKey: false, prefixRules: [] }) } }, t('newGame')),
            h('button', { className: 'ue-btn', onClick: async () => { try { const result = await api('/config/import', {}); push('FModel: ' + (result.imported || []).join(', ')); await load() } catch (e) { setError(e.message) } } }, t('importFmodel'))),
          config.profiles.map((profile) => h('details', {
            key: profile.name,
            className: 'ue-game',
            open: originalName === profile.name,
            onToggle: (event) => {
              // React dispatches toggle for nested <details> (the tag / prefix folds) up the tree
              // as well, so a child fold closing must not collapse this card.
              if (event.target !== event.currentTarget) return
              if (event.target.open && originalName !== profile.name) selectProfile(profile)
              else if (!event.target.open && originalName === profile.name) { setDraft(null); setOriginalName(null) }
            }
          },
            h('summary', null, h(GameBadge, { name: profile.name, src: icons[profile.name] }), profile.name),
            originalName === profile.name ? renderDraft() : null
          )),
          draft && originalName === null ? h('details', { className: 'ue-game', open: true }, h('summary', null, t('newGame')), renderDraft()) : null
        ),
        h('div', { className: 'ue-section' },
          h('h4', null, t('global')),
          h('div', { className: 'ue-field' }, h('label', null, t('outputRoot')), h('input', { value: global.outputRoot, onChange: (event) => setGlobal({ ...global, outputRoot: event.target.value }) })),
          h('div', { className: 'ue-field' }, h('label', null, '缓存 / 索引目录'), h('input', { value: global.cacheRoot, placeholder: '留空使用默认目录', onChange: (event) => setGlobal({ ...global, cacheRoot: event.target.value }) })),
          h('div', { className: 'ue-field' }, h('label', null, t('oodle')),
            h('div', { style: { display: 'flex', gap: '6px' } },
              h('input', { style: { flex: '1' }, value: global.oodleLibrary, placeholder: '留空自动查找', onChange: (event) => setGlobal({ ...global, oodleLibrary: event.target.value }) }),
              h('button', { className: 'ue-btn', disabled: oodleBusy, title: 'Oodle 为 Epic 专有组件：优先使用本机副本，找不到才从公开社区镜像下载', onClick: acquireOodle }, oodleBusy ? '下载中…' : '下载')
            )),
          h('div', { className: 'ue-field' }, h('label', null, t('meshFormat')),
            h('select', { value: global.defaultMeshFormat, onChange: (event) => setGlobal({ ...global, defaultMeshFormat: event.target.value }) },
              ['UEFormat', 'Gltf2', 'ActorX'].map((value) => h('option', { key: value, value }, value)))),
          h('button', { className: 'ue-btn primary', onClick: saveGlobal }, t('save')),
          error ? h('div', { className: 'ue-banner' }, error) : null
        ),
        toast ? h('div', { className: 'ue-toast' }, toast) : null
      )
    }

    function JobBar() {
      const [jobs, setJobs] = useState([]);
      useEffect(() => {
        let alive = true;
        let timer = null;
        const poll = async () => {
          try {
            const list = await api('/jobs');
            if (!alive) return;
            const active = (list || []).filter((job) => job.status === 'running' || job.status === 'queued');
            setJobs(active);
            timer = setTimeout(poll, active.length > 0 ? 1500 : 2500);
          } catch {
            timer = setTimeout(poll, 2500);
          }
        };
        poll();
        return () => { alive = false; if (timer) clearTimeout(timer); };
      }, []);

      if (jobs.length === 0) return null;
      const job = jobs[0];
      return h('div', { className: 'ue-root', style: { background: 'transparent' } },
        h('div', { className: 'ue-jobs' },
          h('span', null, t('jobRunning') + ' ' + (job.completed || 0) + '/' + (job.total || 0)),
          h('div', { className: 'ue-bar' }, h('i', { style: { width: Math.max(2, job.percent || 0) + '%' } })),
          jobs.length > 1 ? h('span', null, '+' + (jobs.length - 1)) : null
        )
      );
    }

    // ── Tool cards ──────────────────────────────────────────────────────────

    function toolCard(props) {
      const block = props.block || {};
      const content = block.content || (block.resultView && block.resultView.content) || [];
      const texts = content.filter((item) => item.type === 'text').map((item) => item.text);
      const images = content.filter((item) => item.type === 'image');
      return h('div', { className: 'ue-card' },
        h('div', { style: { fontWeight: 600 } }, (block.call && block.call.args && (block.call.args.path || block.call.args.profile)) || 'UE asset'),
        texts.length > 0 ? h('div', { className: 'ue-meta', style: { marginTop: 4, whiteSpace: 'pre-wrap', maxHeight: 160, overflow: 'auto' } }, texts.join('\n').slice(0, 1200)) : null,
        images.length > 0 ? h('div', { className: 'ue-hint', style: { marginTop: 4 } }, 'image attached in the tool result') : null
      );
    }

    // ── settings nav icon ───────────────────────────────────────────────────
    // dsh 的 navIcon() 是硬编码的，settings.section 注册没有 icon 槽位，
    // 所以只能等设置面板渲染后，把「UE资产设置」那一行的齿轮 SVG 换成我们的标记。
    const NAV_MARK_CLASS = 'ue-nav-mark';
    const NAV_LABELS = ['UE资产设置', 'UE asset settings'];

    function ueNavMark() {
      const el = document.createElement('span');
      el.className = 'ue-ue-mark ' + NAV_MARK_CLASS;
      el.setAttribute('aria-hidden', 'true');
      return el;
    }

    function patchSettingsNav() {
      const nav = document.querySelector('[role="dialog"] nav');
      if (!nav) return;
      const buttons = nav.querySelectorAll('button');
      for (const btn of buttons) {
        const text = (btn.textContent || '').trim();
        if (!NAV_LABELS.some((label) => text.indexOf(label) >= 0)) continue;
        if (btn.querySelector('.' + NAV_MARK_CLASS)) continue;
        const icon = btn.firstElementChild;
        if (icon && icon.tagName && icon.tagName.toLowerCase() === 'svg') icon.replaceWith(ueNavMark());
        else btn.insertBefore(ueNavMark(), btn.firstChild);
      }
    }

    // React 重渲染会把齿轮写回去，所以挂一个观察器持续校正。
    function watchSettingsNav() {
      let queued = false;
      const run = () => {
        queued = false;
        try { patchSettingsNav(); } catch (err) {}
      };
      const schedule = () => {
        if (queued) return;
        queued = true;
        if (typeof window !== 'undefined' && window.requestAnimationFrame) window.requestAnimationFrame(run);
        else setTimeout(run, 16);
      };
      schedule();
      const observer = new MutationObserver(schedule);
      observer.observe(document.body, { childList: true, subtree: true });
      return () => observer.disconnect();
    }

    // ── apply ───────────────────────────────────────────────────────────────

    function apply(ctx) {
      ctx.effect(() => installStyle(), 'dsh-ue-asset: styles');
      ctx.effect(() => watchSettingsNav(), 'dsh-ue-asset: settings nav icon');

      // Right-sidebar tab: both services are optional, so probe them instead of injecting;
      // an older host simply keeps the tools + settings page and never mounts the tab body.
      if (ctx.slots) {
        ctx.slots.inject('sidebar.right.pane.tab', () => {
          let disposed = false;
          let timer = null;
          let disposeTabs = null;
          let disposeBody = null;

          const attempt = () => {
            if (disposed) return;
            const tabs = typeof ctx.get === 'function' ? ctx.get('sidebarRightTabs') : null;
            const controller = typeof ctx.get === 'function' ? ctx.get('sidebarRight') : null;
            if (!tabs || typeof tabs.register !== 'function' || !controller || typeof controller.openTab !== 'function') {
              timer = setTimeout(attempt, 250);
              return;
            }

            try {
              disposeTabs = tabs.register({
                id: TAB_ID,
                kind: TAB_ID,
                keepMounted: true,
                title: () => t('title'),
                guide: [{
                  id: 'browser',
                  order: 120,
                  title: () => t('title'),
                  description: () => t('settings')
                }]
              });
              disposeBody = ctx.slots.register(
                { name: 'sidebar.right.pane.tab', key: TAB_ID },
                () => h(UePanel)
              );
            } catch (error) {
              console.warn('[ue-asset] sidebar registration rejected:', error && error.message ? error.message : error);
              try { disposeTabs && disposeTabs(); } catch {}
              try { disposeBody && disposeBody(); } catch {}
              disposeTabs = null;
              disposeBody = null;
            }
          };

          attempt();
          return () => {
            disposed = true;
            if (timer) clearTimeout(timer);
            try { disposeBody && disposeBody(); } catch {}
            try { disposeTabs && disposeTabs(); } catch {}
          };
        });

        ctx.slots.inject('settings.section', () => ctx.slots.register(
          { name: 'settings.section', id: 'ue-asset', order: 30, label: () => t('settings') },
          () => h(SettingsSection)
        ));

        ctx.slots.inject('shell.bottom', () => ctx.slots.register(
          { name: 'shell.bottom', id: 'ue-asset-jobs', order: 80 },
          () => h(JobBar)
        ));

      }
    }

    exports.apply = apply;
    exports.inject = ['slots'];
    return module.exports;
  }
});
