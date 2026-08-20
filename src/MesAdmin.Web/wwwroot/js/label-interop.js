// 追溯标签交互：真实二维码渲染 + 打印。
// 二维码由本地内置的 qrcode-generator（Kazuhiko Arase, MIT）生成，不依赖外部 CDN（工厂离线可用）。
window.labelInterop = {
    // 渲染真实二维码到指定容器（canvas，尺寸与容器匹配）
    renderQr: function (containerId, text) {
        var container = document.getElementById(containerId);
        if (!container) { console.error('labelInterop: 容器不存在', containerId); return false; }

        try {
            var qr = qrcode(0, 'M'); // 类型 0=自动选择, 纠错级别 M
            qr.addData(text);
            qr.make();

            var cellSize = 3;
            var size = qr.getModuleCount() * cellSize;
            var canvas = document.createElement('canvas');
            canvas.width = size;
            canvas.height = size;
            canvas.style.width = '100%';
            canvas.style.height = '100%';

            var ctx = canvas.getContext('2d');
            var modCount = qr.getModuleCount();
            for (var r = 0; r < modCount; r++) {
                for (var c = 0; c < modCount; c++) {
                    ctx.fillStyle = qr.isDark(r, c) ? '#000' : '#fff';
                    ctx.fillRect(c * cellSize, r * cellSize, cellSize, cellSize);
                }
            }

            container.innerHTML = '';
            container.appendChild(canvas);
            return true;
        } catch (e) {
            console.error('labelInterop: 二维码渲染失败', e);
            container.textContent = text;
            return false;
        }
    },

    // 打印指定选择器的内容（克隆到隐藏 iframe，避免污染页面样式）
    printElement: function (selector) {
        var src = document.querySelector(selector);
        if (!src) { console.error('labelInterop: 打印源不存在', selector); return false; }

        var frame = document.createElement('iframe');
        frame.style.position = 'fixed';
        frame.style.right = '0';
        frame.style.bottom = '0';
        frame.style.width = '0';
        frame.style.height = '0';
        frame.style.border = '0';
        document.body.appendChild(frame);

        var doc = frame.contentWindow.document;
        doc.write('<html><head><title>追溯标签</title><style>');
        doc.write('body{font-family:monospace;margin:0;padding:8px;}');
        doc.write('*{box-sizing:border-box;}');
        doc.write('</style></head><body>');
        doc.write(src.innerHTML);
        doc.write('</body></html>');
        doc.close();

        frame.onload = function () {
            frame.contentWindow.focus();
            frame.contentWindow.print();
            setTimeout(function () { frame.remove(); }, 500);
        };
        // 某些浏览器 onload 已触发过，主动触发一次
        if (frame.contentWindow.document.readyState === 'complete') frame.onload();
        return true;
    }
};