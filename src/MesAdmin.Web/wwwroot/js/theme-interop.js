// AutoMES 主题偏好持久化 + <html data-mes-theme> 属性同步（供 app.css 令牌分组）
window.mesTheme = {
    get: function () {
        try { return localStorage.getItem('mes.theme'); } catch (e) { return null; }
    },
    set: function (mode) {
        try { localStorage.setItem('mes.theme', mode); } catch (e) { /* 隐私模式等场景忽略 */ }
        document.documentElement.setAttribute('data-mes-theme', mode);
    }
};
