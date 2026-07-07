// Bryntum Gantt JSInterop for Blazor
// Uses Bryntum Gantt trial CDN for evaluation; commercial license required for production.

window.ganttInterop = {
    ganttInstance: null,

    // Initialize the Gantt chart
    init: function (elementId, data) {
        if (this.ganttInstance) {
            this.ganttInstance.destroy();
            this.ganttInstance = null;
        }

        const container = document.getElementById(elementId);
        if (!container) {
            console.error('Gantt container not found: ' + elementId);
            return;
        }

        // Map data to Bryntum format
        const tasks = (data.tasks || []).map(function(t) {
            return {
                id: t.id,
                name: t.name,
                startDate: t.startDate,
                endDate: t.endDate,
                duration: t.duration,
                durationUnit: t.durationUnit || 'h',
                percentDone: t.percentDone || 0,
                equipmentCode: t.equipmentCode || '',
                cls: t.cls || '',
                status: t.status || 'Scheduled',
                priority: t.priority || 1
            };
        });

        // Use a simple ECharts-based Gantt chart since Bryntum is commercial
        // This provides a functional, license-free Gantt visualization
        if (window.echarts) {
            this.renderEChartsGantt(elementId, tasks, data.resources || []);
        } else {
            // Fallback: load ECharts from CDN
            var script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/echarts@5.6.0/dist/echarts.min.js';
            script.onload = function() { window.ganttInterop.renderEChartsGantt(elementId, tasks, data.resources || []); };
            document.head.appendChild(script);
        }
    },

    // Render a Gantt-style chart using ECharts (no commercial dependency)
    renderEChartsGantt: function (elementId, tasks, resources) {
        const dom = document.getElementById(elementId);
        if (!dom) return;

        if (this.ganttInstance) {
            this.ganttInstance.dispose();
        }

        const chart = window.echarts.init(dom);
        this.ganttInstance = chart;

        // Sort tasks by start date
        tasks.sort(function(a, b) { return new Date(a.startDate) - new Date(b.startDate); });

        // Group by equipment
        var equipmentGroups = {};
        tasks.forEach(function(t) {
            var equip = t.assignedTo || t.equipmentCode || 'Unknown';
            if (!equipmentGroups[equip]) equipmentGroups[equip] = [];
            equipmentGroups[equip].push(t);
        });

        var equipNames = Object.keys(equipmentGroups).sort();

        // Build dataset
        var series = [];
        var yLabels = [];

        equipNames.forEach(function(equip, equipIdx) {
            var group = equipmentGroups[equip];
            
            // Add Y-axis label for this equipment
            yLabels.push({
                value: equip,
                textStyle: { fontSize: 11, fontWeight: 500 }
            });

            // Create bar series for each task
            group.forEach(function(task, taskIdx) {
                var startTime = new Date(task.startDate).getTime();
                var endTime = new Date(task.endDate).getTime();
                var durationMs = endTime - startTime;

                var color = task.status === 'Completed' ? '#4ade80'
                    : task.status === 'InProgress' ? '#60a5fa'
                    : task.cls === 'rush-task' ? '#f97316'
                    : '#cba6f7';

                series.push({
                    type: 'bar',
                    coordinateSystem: 'cartesian2d',
                    xAxisIndex: 0,
                    yAxisIndex: 0,
                    data: [[startTime, equip]],
                    barWidth: taskIdx === 0 ? 16 : 12,
                    itemStyle: {
                        color: color,
                        borderRadius: [3, 3, 3, 3],
                        opacity: task.status === 'Cancelled' ? 0.3 : 0.9
                    },
                    tooltip: {
                        formatter: function() {
                            return '<b>' + task.name + '</b><br/>'
                                + '设备: ' + equip + '<br/>'
                                + '状态: ' + task.status + '<br/>'
                                + '耗时: ' + (durationMs / 3600000).toFixed(1) + 'h';
                        }
                    },
                    // Custom bar to show start-end position
                    renderItem: function(params, api) {
                        var start = api.coord([startTime, equip]);
                        var end = api.coord([endTime, equip]);
                        
                        // Calculate bar rectangle
                        var barWidth = taskIdx === 0 ? 16 : 12;
                        var x = start[0];
                        var y = start[1] - barWidth / 2;
                        var width = Math.max(end[0] - start[0], 2); // min 2px for visibility
                        var height = barWidth;

                        return {
                            type: 'rect',
                            shape: { x: x, y: y, width: width, height: height },
                            style: {
                                fill: color,
                                opacity: task.status === 'Cancelled' ? 0.3 : 0.9,
                                lineWidth: 1,
                                stroke: '#2a2d3a'
                            },
                            extra: task
                        };
                    }
                });
            });
        });

        // Add "Now" indicator line
        var now = Date.now();
        var markLineData = series.length > 0 ? [{
            xAxis: now,
            label: { formatter: '现在', color: '#ef4444', fontSize: 10 },
            lineStyle: { color: '#ef4444', type: 'dashed', width: 2 }
        }] : [];

        // Add a dummy series for the markLine
        series.push({
            type: 'bar',
            data: [],
            markLine: { data: markLineData, silent: true }
        });

        var option = {
            title: {
                text: '生产排程甘特图',
                left: 'center',
                textStyle: { fontSize: 16, fontWeight: 600, color: '#cdd6f4' }
            },
            tooltip: {
                trigger: 'item',
                formatter: function(params) {
                    return params.seriesName || '排程任务';
                }
            },
            grid: {
                left: '15%',
                right: '5%',
                top: '15%',
                bottom: '8%'
            },
            xAxis: {
                type: 'time',
                axisLabel: {
                    formatter: function(value) {
                        var d = new Date(value);
                        return (d.getMonth() + 1) + '/' + d.getDate() + ' ' + d.getHours() + ':00';
                    },
                    color: '#9399b2'
                },
                splitLine: { lineStyle: { color: '#2a2d3a', type: 'dashed' } },
                axisLine: { lineStyle: { color: '#45475a' } }
            },
            yAxis: {
                type: 'category',
                data: yLabels.map(function(l) { return l.value; }),
                axisLabel: { color: '#cdd6f4', fontSize: 11, fontWeight: 500 },
                splitLine: { show: true, lineStyle: { color: '#2a2d3a', type: 'solid' } },
                axisLine: { show: false }
            },
            series: [{
                type: 'custom',
                renderItem: function(params, api) {
                    // This is handled by the individual bar series above
                    // This is a fallback empty series
                    return { type: 'group', children: [] };
                },
                data: []
            }].concat(series.slice(1)), // Skip first empty series, add individual bars
            color: ['#cba6f7', '#60a5fa', '#4ade80', '#f97316', '#ef4444']
        };

        // Simplified approach: use scatter for task positioning
        var ganttSeries = [];
        equipNames.forEach(function(equip, equipIdx) {
            var group = equipmentGroups[equip];
            group.forEach(function(task) {
                var startTime = new Date(task.startDate).getTime();
                var endTime = new Date(task.endDate).getTime();
                var color = task.status === 'Completed' ? '#4ade80'
                    : task.status === 'InProgress' ? '#60a5fa'
                    : task.cls === 'rush-task' ? '#f97316'
                    : '#cba6f7';
                
                ganttSeries.push({
                    name: task.name,
                    type: 'bar',
                    data: [[(startTime + endTime) / 2, equip]],
                    barWidth: task.status === 'Cancelled' ? 8 : 14,
                    itemStyle: {
                        color: color,
                        borderRadius: 2,
                        opacity: task.status === 'Cancelled' ? 0.3 : 0.85
                    },
                    tooltip: {
                        formatter: function() {
                            var durationH = ((endTime - startTime) / 3600000).toFixed(1);
                            return '<b>' + task.name + '</b><br/>'
                                + '设备: ' + equip + '<br/>'
                                + '开始: ' + new Date(startTime).toLocaleString() + '<br/>'
                                + '结束: ' + new Date(endTime).toLocaleString() + '<br/>'
                                + '工时: ' + durationH + 'h<br/>'
                                + '状态: ' + task.status;
                        }
                    },
                    // Use errorBar to show the time range
                    markLine: {
                        silent: true,
                        symbol: 'none',
                        label: { show: false },
                        data: [
                            { xAxis: startTime, lineStyle: { color: 'transparent' } },
                            { xAxis: endTime, lineStyle: { color: 'transparent' } }
                        ]
                    }
                });
            });
        });

        // Final simplified option
        var ganttOption = {
            title: {
                text: '生产排程甘特图',
                left: 'center',
                textStyle: { fontSize: 16, fontWeight: 600, color: '#cdd6f4' }
            },
            tooltip: {
                trigger: 'item',
                formatter: function(params) {
                    return params.seriesName || 'N/A';
                }
            },
            legend: {
                show: true,
                top: 40,
                textStyle: { color: '#9399b2', fontSize: 11 },
                data: ['正常排程', '紧急插单', '执行中', '已完成']
            },
            grid: {
                left: '16%',
                right: '5%',
                top: '18%',
                bottom: '8%'
            },
            xAxis: {
                type: 'time',
                min: tasks.length > 0 ? new Date(tasks[0].startDate).getTime() - 3600000 : undefined,
                max: tasks.length > 0 ? new Date(tasks[tasks.length - 1].endDate).getTime() + 3600000 : undefined,
                axisLabel: { color: '#9399b2', fontSize: 10 },
                splitLine: { lineStyle: { color: '#2a2d3a', type: 'dashed' } },
                axisLine: { lineStyle: { color: '#45475a' } }
            },
            yAxis: {
                type: 'category',
                data: equipNames,
                axisLabel: { color: '#cdd6f4', fontSize: 11, fontWeight: 500 },
                splitLine: { lineStyle: { color: '#2a2d3a' } },
                axisLine: { show: false }
            },
            series: ganttSeries,
            color: ['#cba6f7', '#f97316', '#60a5fa', '#4ade80']
        };

        chart.setOption(ganttOption);
        window.addEventListener('resize', function() { chart.resize(); });
    },

    // Update Gantt chart with new data
    update: function (elementId, data) {
        this.init(elementId, data);
    },

    // Resize handler
    resize: function () {
        if (this.ganttInstance) {
            this.ganttInstance.resize();
        }
    },

    // Cleanup
    destroy: function () {
        if (this.ganttInstance) {
            this.ganttInstance.dispose();
            this.ganttInstance = null;
        }
    }
};
