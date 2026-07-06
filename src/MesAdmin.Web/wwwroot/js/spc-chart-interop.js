// SPC X̄-R 控制图 ECharts 渲染
// 供 Blazor JSInterop 调用

window.spcCharts = {
    charts: {},

    // 初始化 X̄-R 控制图
    initXbarRChart: function (chartId, data) {
        if (!window.echarts) {
            console.error('ECharts not loaded');
            return;
        }

        if (this.charts[chartId]) {
            this.charts[chartId].dispose();
        }

        const dom = document.getElementById(chartId);
        if (!dom) {
            console.error('Chart container not found: ' + chartId);
            return;
        }

        const chart = window.echarts.init(dom);
        this.charts[chartId] = chart;

        const option = this.buildXbarROption(data);
        chart.setOption(option);
        window.addEventListener('resize', () => chart.resize());
    },

    // 更新已有图表
    updateXbarRChart: function (chartId, data) {
        const chart = this.charts[chartId];
        if (!chart) {
            this.initXbarRChart(chartId, data);
            return;
        }
        const option = this.buildXbarROption(data);
        chart.setOption(option);
    },

    // 构建 X̄-R 图配置
    buildXbarROption: function (data) {
        const subgroupIndices = data.samples.map(s => s.subgroupIndex);
        const means = data.samples.map(s => s.mean);
        const ranges = data.samples.map(s => s.range);

        // 控制限线
        const cl = data.centerLine;
        const ucl = data.upperControlLimit;
        const lcl = data.lowerControlLimit;
        const uclR = data.upperRangeLimit;
        const clR = data.centerRange;

        // 控制限常量线
        const clLine = subgroupIndices.map(() => cl);
        const uclLine = subgroupIndices.map(() => ucl);
        const lclLine = subgroupIndices.map(() => lcl);
        const uclRLine = subgroupIndices.map(() => uclR);
        const clRLine = subgroupIndices.map(() => clR);

        // 超出控制限的点
        const outOfControlMeans = [];
        const outOfControlRanges = [];
        data.samples.forEach((s, i) => {
            if (s.mean > ucl || s.mean < lcl) {
                outOfControlMeans.push([subgroupIndices[i], s.mean]);
            }
            if (s.range > uclR) {
                outOfControlRanges.push([subgroupIndices[i], s.range]);
            }
        });

        // Zone 着色区域
        const zoneAUpper = subgroupIndices.map(() => cl + (ucl - cl) * 2 / 3);
        const zoneALower = subgroupIndices.map(() => cl - (ucl - cl) * 2 / 3);

        return {
            title: [
                {
                    text: data.characteristicName + ' — X̄ 控制图',
                    left: 'center',
                    textStyle: { fontSize: 14, fontWeight: 600 }
                },
                {
                    text: 'Cpk: ' + (data.cpk != null ? data.cpk.toFixed(4) : 'N/A'),
                    right: 20,
                    top: 0,
                    textStyle: { fontSize: 12, color: '#9399B2' }
                }
            ],
            tooltip: {
                trigger: 'axis',
                formatter: function (params) {
                    let tip = '<b>子组 ' + params[0].axisValue + '</b><br/>';
                    params.forEach(p => {
                        tip += p.marker + ' ' + p.seriesName + ': ' + p.data.toFixed(4) + '<br/>';
                    });
                    return tip;
                }
            },
            legend: {
                data: ['X̄', 'CL', 'UCL', 'LCL', '超出控制限'],
                top: 30,
                textStyle: { fontSize: 11 }
            },
            grid: { left: '5%', right: '5%', top: '55%', bottom: '5%' },
            xAxis: {
                type: 'category',
                data: subgroupIndices,
                name: '子组',
                nameLocation: 'center',
                nameGap: 25
            },
            yAxis: {
                type: 'value',
                name: data.unit || '',
                min: lcl - (ucl - lcl) * 0.2,
                max: ucl + (ucl - lcl) * 0.2,
                splitLine: { lineStyle: { color: '#2A2D3A', type: 'dashed' } }
            },
            visualMap: {
                show: false,
                pieces: [
                    { min: lcl, max: ucl, color: '#1a1d2e' },
                    { lt: lcl, color: '#ef4444' },
                    { gt: ucl, color: '#ef4444' }
                ],
                seriesIndex: 0,
                dimension: 1
            },
            series: [
                {
                    name: 'X̄',
                    type: 'line',
                    data: means,
                    symbol: 'circle',
                    symbolSize: 6,
                    lineStyle: { width: 1.5, color: '#CBA6F7' },
                    itemStyle: { color: '#CBA6F7' },
                    markLine: {
                        silent: true,
                        data: [
                            { yAxis: ucl, label: { formatter: 'UCL: ' + ucl.toFixed(2), color: '#ef4444' }, lineStyle: { color: '#ef4444', type: 'dashed' } },
                            { yAxis: cl, label: { formatter: 'CL: ' + cl.toFixed(2), color: '#4ade80' }, lineStyle: { color: '#4ade80', type: 'solid' } },
                            { yAxis: lcl, label: { formatter: 'LCL: ' + lcl.toFixed(2), color: '#ef4444' }, lineStyle: { color: '#ef4444', type: 'dashed' } }
                        ]
                    },
                    markPoint: {
                        data: outOfControlMeans.map(p => ({
                            coord: p,
                            symbol: 'pin',
                            symbolSize: 30,
                            itemStyle: { color: '#ef4444' },
                            label: { show: false }
                        }))
                    }
                }
            ]
        };
    },

    // 初始化 R 控制图（极差图）
    initRChart: function (chartId, data) {
        if (!window.echarts) {
            console.error('ECharts not loaded');
            return;
        }

        if (this.charts[chartId]) {
            this.charts[chartId].dispose();
        }

        const dom = document.getElementById(chartId);
        if (!dom) {
            console.error('Chart container not found: ' + chartId);
            return;
        }

        const chart = window.echarts.init(dom);
        this.charts[chartId] = chart;

        const option = this.buildROption(data);
        chart.setOption(option);
        window.addEventListener('resize', () => chart.resize());
    },

    // 构建 R 图配置
    buildROption: function (data) {
        const subgroupIndices = data.samples.map(s => s.subgroupIndex);
        const ranges = data.samples.map(s => s.range);

        const clR = data.centerRange;
        const uclR = data.upperRangeLimit;

        // 超出 R 控制限的点
        const outOfControlRanges = [];
        data.samples.forEach((s, i) => {
            if (s.range > uclR) {
                outOfControlRanges.push([subgroupIndices[i], s.range]);
            }
        });

        return {
            title: {
                text: data.characteristicName + ' — R 控制图',
                left: 'center',
                textStyle: { fontSize: 14, fontWeight: 600 }
            },
            tooltip: {
                trigger: 'axis',
                formatter: function (params) {
                    let tip = '<b>子组 ' + params[0].axisValue + '</b><br/>';
                    params.forEach(p => {
                        tip += p.marker + ' ' + p.seriesName + ': ' + p.data.toFixed(4) + '<br/>';
                    });
                    return tip;
                }
            },
            legend: {
                data: ['R', 'CL', 'UCL'],
                top: 30,
                textStyle: { fontSize: 11 }
            },
            grid: { left: '5%', right: '5%', top: '15%', bottom: '5%' },
            xAxis: {
                type: 'category',
                data: subgroupIndices,
                name: '子组',
                nameLocation: 'center',
                nameGap: 25
            },
            yAxis: {
                type: 'value',
                name: data.unit || '',
                min: 0,
                max: uclR * 1.3,
                splitLine: { lineStyle: { color: '#2A2D3A', type: 'dashed' } }
            },
            series: [
                {
                    name: 'R',
                    type: 'line',
                    data: ranges,
                    symbol: 'diamond',
                    symbolSize: 6,
                    lineStyle: { width: 1.5, color: '#60A5FA' },
                    itemStyle: { color: '#60A5FA' },
                    markLine: {
                        silent: true,
                        data: [
                            { yAxis: uclR, label: { formatter: 'UCL: ' + uclR.toFixed(2), color: '#ef4444' }, lineStyle: { color: '#ef4444', type: 'dashed' } },
                            { yAxis: clR, label: { formatter: 'CL: ' + clR.toFixed(2), color: '#4ade80' }, lineStyle: { color: '#4ade80', type: 'solid' } }
                        ]
                    },
                    markPoint: {
                        data: outOfControlRanges.map(p => ({
                            coord: p,
                            symbol: 'pin',
                            symbolSize: 30,
                            itemStyle: { color: '#ef4444' },
                            label: { show: false }
                        }))
                    }
                }
            ]
        };
    },

    // 销毁图表
    disposeChart: function (chartId) {
        if (this.charts[chartId]) {
            this.charts[chartId].dispose();
            delete this.charts[chartId];
        }
    }
};
