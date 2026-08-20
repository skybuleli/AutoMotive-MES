// 质量报表 PDF 交互：把 base64 字节转 Blob，触发下载或新窗口查看。
window.reportInterop = {
    toBlobUrl: function (base64) {
        try {
            var binary = atob(base64);
            var len = binary.length;
            var bytes = new Uint8Array(len);
            for (var i = 0; i < len; i++) {
                bytes[i] = binary.charCodeAt(i);
            }
            return URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
        } catch (e) {
            console.error('reportInterop: base64 解码失败', e);
            return null;
        }
    },

    downloadPdf: function (base64, fileName) {
        var url = window.reportInterop.toBlobUrl(base64);
        if (!url) return false;
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 5000);
        return true;
    },

    openPdf: function (base64) {
        var url = window.reportInterop.toBlobUrl(base64);
        if (!url) return false;
        window.open(url, '_blank');
        // 新标签页持有该 URL，延迟回收以兼容浏览器加载时序
        setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
        return true;
    }
};
