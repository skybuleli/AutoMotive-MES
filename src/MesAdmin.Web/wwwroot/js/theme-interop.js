// AutoMES 主题偏好持久化 + <html data-mes-theme> 属性同步（供 app.css 令牌分组）
// 注：App.razor 首帧内联脚本会在 DOMContentLoaded 时设置 body 内联背景（防白闪），
//     此处切换时同步更新内联背景，避免内联样式冻结在旧颜色上。
window.mesTheme = {
    get: function () {
        try { return localStorage.getItem('mes.theme'); } catch (e) { return null; }
    },
    set: function (mode) {
        try { localStorage.setItem('mes.theme', mode); } catch (e) { /* 隐私模式等场景忽略 */ }
        document.documentElement.setAttribute('data-mes-theme', mode);
        if (document.body) {
            document.body.style.background = mode === 'light' ? '#F0F4E6' : '#191925';
        }
    }
};
