// OEE 趋势图 ECharts 渲染（M05 设备综合效率）
// 供 Blazor JSInterop 调用；颜色在每次 setOption 时从 CSS 变量读取，
// 明暗主题切换后下一个数据节拍自动跟随，无需重建图表。

window.oeeTrend = {
    charts: {},

    // 主题色板：每次渲染时从 CSS 自定义属性解析（--mes-* 随 data-mes-theme 切换）
    palette: function () {
        const css = getComputedStyle(document.documentElement);
        const v = (name, fallback) => (css.getPropertyValue(name) || '').trim() || fallback;
        return {
            lav: v('--mes-lav', '#CBA6F7'),
            sky: v('--mes-sky', '#93BBFB'),
            mint: v('--mes-mint', '#ADE6A8'),
            gold: v('--mes-gold', '#FBE7B6'),
            rose: v('--mes-rose', '#F595B0'),
            text2: v('--mes-text-2', '#BFC3DA'),
            text3: v('--mes-text-3', '#9096B0'),
            split: v('--mes-border', 'rgba(255,255,255,0.10)'),
            surface: v('--mes-surface', '#252533'),
            mono: v('--mes-mono', "'JetBrains Mono', monospace"),
        };
    },

    ensureChart: function (chartId) {
        if (this.charts[chartId]) return this.charts[chartId];
        const dom = document.getElementById(chartId);
        if (!dom || !window.echarts) return null;
        const chart = window.echarts.init(dom);
        this.charts[chartId] = chart;
        return chart;
    },

    init: function (chartId, payload) {
        const chart = this.ensureChart(chartId);
        if (!chart) return;
        chart.setOption(this.buildOption(payload));
    },

    update: function (chartId, payload) {
        const chart = this.charts[chartId];
        if (!chart || chart.isDisposed()) {
            this.init(chartId, payload);
            return;
        }
        // notMerge=false 增量合并：仅替换数据序列，保留交互状态
        chart.setOption(this.buildOption(payload));
    },

    resizeAll: function () {
        Object.values(this.charts).forEach(c => { if (!c.isDisposed()) c.resize(); });
    },

    dispose: function (chartId) {
        if (this.charts[chartId]) {
            this.charts[chartId].dispose();
            delete this.charts[chartId];
        }
    },

    buildOption: function (data) {
        const p = this.palette();
        const series = [
            { name: 'OEE', values: data.oee, color: p.lav, area: true, width: 2.5 },
            { name: '可用率', values: data.availability, color: p.sky },
            { name: '性能率', values: data.performance, color: p.gold },
            { name: '良品率', values: data.quality, color: p.mint },
        ];

        return {
            animationDurationUpdate: 300,
            tooltip: {
                trigger: 'axis',
                backgroundColor: p.surface,
                borderColor: p.split,
                textStyle: { color: p.text2, fontSize: 12, fontFamily: p.mono },
                valueFormatter: value => (value == null ? '--' : value.toFixed(1) + '%'),
            },
            legend: {
                top: 0,
                right: 0,
                icon: 'roundRect',
                itemWidth: 14,
                itemHeight: 4,
                textStyle: { color: p.text2, fontSize: 11 },
            },
            grid: { left: 8, right: 16, top: 32, bottom: 0, containLabel: true },
            xAxis: {
                type: 'category',
                boundaryGap: false,
                data: data.times,
                axisLine: { lineStyle: { color: p.split } },
                axisTick: { show: false },
                axisLabel: { color: p.text3, fontSize: 10, fontFamily: p.mono, hideOverlap: true },
            },
            yAxis: {
                type: 'value',
                min: 0,
                max: 100,
                interval: 25,
                axisLabel: { color: p.text3, fontSize: 10, fontFamily: p.mono, formatter: '{value}%' },
                splitLine: { lineStyle: { color: p.split, type: 'dashed' } },
            },
            series: series.map(s => ({
                name: s.name,
                type: 'line',
                data: s.values,
                smooth: 0.35,
                symbol: 'none',
                lineStyle: { width: s.width || 1.5, color: s.color },
                itemStyle: { color: s.color },
                emphasis: { focus: 'series' },
                areaStyle: s.area ? {
                    color: {
                        type: 'linear', x: 0, y: 0, x2: 0, y2: 1,
                        colorStops: [
                            { offset: 0, color: this.rgba(s.color, 0.28) },
                            { offset: 1, color: this.rgba(s.color, 0) },
                        ],
                    },
                } : undefined,
                markLine: s.name === 'OEE' ? {
                    silent: true,
                    symbol: 'none',
                    label: { position: 'insideEndTop', fontSize: 10, fontFamily: p.mono },
                    data: [
                        { yAxis: 85, lineStyle: { color: p.mint, type: 'dashed', width: 1 }, label: { formatter: '目标 85%', color: p.mint } },
                        { yAxis: 70, lineStyle: { color: p.rose, type: 'dashed', width: 1 }, label: { formatter: '报警 70%', color: p.rose } },
                    ],
                } : undefined,
            })),
        };
    },

    // '#RRGGBB' → 'rgba(r,g,b,a)'（markLine/areaStyle 需要透明度变体）
    rgba: function (hex, alpha) {
        const m = hex.match(/^#?([0-9a-f]{6})$/i);
        if (!m) return hex;
        const n = parseInt(m[1], 16);
        return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${alpha})`;
    },
};

// 全局只注册一次 resize 广播
window.addEventListener('resize', () => window.oeeTrend.resizeAll());
