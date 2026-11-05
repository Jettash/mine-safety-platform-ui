# 矿车载运安全监测系统 UI

基于 Vue 3、TypeScript、Vite 和 Pinia 的前端界面，包含登录、综合监控、历史记录、事件详情、报警处理、人工复核和设备状态页面。

## 本地启动

需要 Node.js 20.19+ 或 22.12+。

```bash
npm ci
npm run dev
```

打开 http://localhost:5173/ 。开发服务器将 `/api` 和 `/ws` 请求代理到 `127.0.0.1:8080`，登录和业务功能需要启动后端。

后端及完整项目：https://github.com/Jettash/mine-safety-platform

## 构建

```bash
npm run build
npm run preview
```

构建产物位于 `dist/`。
