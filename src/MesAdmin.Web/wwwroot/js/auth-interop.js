// 登录后把 JWT 同步到普通 cookie：整页导航时服务端 JWT Bearer 在
// OnMessageReceived 里从该 cookie 兜底取 token，通过 [Authorize] 校验。
// JWT 原本就存在 localStorage，此处不额外扩大 XSS 面。
window.mesAuth = {
    setCookie: function (name, value) {
        var d = new Date();
        d.setTime(d.getTime() + 30 * 24 * 60 * 60 * 1000); // 30 天
        document.cookie = name + "=" + encodeURIComponent(value)
            + "; expires=" + d.toUTCString() + "; path=/; SameSite=Lax";
    },
    clearCookie: function (name) {
        document.cookie = name + "=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/";
    }
};
