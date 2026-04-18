# SGMDTXTools API Reference

Python 感知服务 base URL: `http://127.0.0.1:5100`

## 感知接口

### POST /api/ocr
OCR 文字识别。

请求: multipart/form-data
- `image`: PNG 文件
- `region` (可选): JSON `{"x":0,"y":0,"width":100,"height":50}`

响应:
```json
{
  "success": true,
  "elapsed_ms": 800,
  "image_size": {"width": 1024, "height": 512},
  "texts": [
    {"text": "领地60/60", "confidence": 0.99, "bbox": {"x": 186, "y": 3, "width": 68, "height": 14}, "center": {"x": 220, "y": 10}}
  ]
}
```

### POST /api/match
模板匹配。

请求: multipart/form-data
- `image`: PNG 文件
- `templates` (可选): JSON 数组 `["btn_wujiang", "icon_lingdi"]`

响应:
```json
{
  "success": true,
  "elapsed_ms": 1500,
  "matches": [
    {"template": "btn_wujiang", "confidence": 0.97, "bbox": {"x": 498, "y": 440, "width": 78, "height": 58}, "center": {"x": 537, "y": 469}}
  ]
}
```

### POST /api/scan
OCR + 模板匹配组合，一次调用完成全部感知。

请求: multipart/form-data
- `image`: PNG 文件

响应: 同时包含 `texts` 和 `matches` 字段。

### GET /api/health
健康检查。

响应: `{"status": "ok", "version": "0.1.0", "ocr_ready": true, "template_count": 9}`

## 模板管理接口

### GET /api/templates
列出所有模板。

### POST /api/templates
从截图裁剪创建新模板。

请求: JSON
```json
{
  "name": "btn_confirm",
  "screenshot": "capture_20260418_101403_763.png",
  "region": {"x": 100, "y": 200, "width": 80, "height": 40},
  "category": "button",
  "threshold": 0.8,
  "description": "确认按钮"
}
```

### DELETE /api/templates/{name}
删除模板。

### POST /api/templates/{name}/test
在指定截图上测试模板匹配效果。

请求: JSON `{"screenshot": "capture_xxx.png"}`

## 截图接口

### GET /api/screenshots
列出所有截图，按时间倒序。

### GET /api/screenshots/{filename}
下载指定截图文件。
