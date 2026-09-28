# 资源站 API 接入规范

> 本管理器（ADOFAI Mod Manager）**不绑定任何单一资源站**。
> 任何站点只要实现下面这套 HTTP 接口，用户就能在「设置 → 资源站」里填上你的地址直接使用。

---

## 1. 概述

| 项目 | 说明 |
|---|---|
| 用户配置 | 「设置 → 资源站」里填 **站点地址（base URL）**，例如 `https://www.example.com` |
| 实际请求 | 管理器会请求 `{base}/api/...`（若你填的地址已经以 `/api` 结尾则不再追加） |
| 协议 | HTTPS（推荐）。也支持 HTTP，但会明文传输 API key |
| 认证 | `Authorization: Bearer <API_KEY>`；**如果站点不需要鉴权，可以完全不校验**（key 允许留空） |
| User-Agent | `AdofaiModManager/0.1` |
| 编码 | UTF-8；响应 `Content-Type: application/json` |
| 错误格式 | 建议 `{"statusCode":401,"message":"...","error":"Unauthorized"}`；管理器会读取 `message` 展示给用户 |

> 兼容性提示：管理器的行为对齐 **ADOFAI Tools**（`https://www.adofaitools.top`）。照它的行为实现即可。

---

## 2. 必需接口（3 个，实现这 3 个就能用）

### 2.1 列出 Mod

```
GET /api/mods?page=1&pageSize=20&search=&resourceType=&loader=&sort=updated&featured=
```

查询参数（都可选）：

| 参数 | 说明 |
|---|---|
| `page` | 页码，从 1 开始 |
| `pageSize` | 每页数量，**最大 100**（管理器的"检查更新"会一次拉 100 条） |
| `search` | 关键词（对名称/简介做模糊匹配即可） |
| `resourceType` | 见 [§5 取值约定](#5-取值约定) |
| `loader` | 加载器名，如 `unitymodmanager` |
| `sort` | `updated`（默认，最近更新） / `downloads` / `favorites` |
| `featured` | `true` 时只返回精选 |

响应：

```jsonc
{
  "items": [
    {
      "id": "全局唯一 ID（字符串，UUID 或自增都行）",
      "resourceType": "MOD",
      "slug": "用于详情接口的短名（建议 URL 安全）",
      "displayName": "显示名称（重要：尽量与 mod 的 UMM Id 一致，见 §6）",
      "summary": "一句话简介",
      "authors": [
        { "name": "作者名", "nickname": "昵称", "avatarUrl": "https://..." }
      ],
      "iconUrl": "https://.../icon.png",
      "downloadCount": 123,
      "favoriteCount": 4,
      "latestVersion": { "versionId": "1.2.3", "publishedAt": "2026-01-02T03:04:05Z" }
    }
  ],
  "total": 121,
  "page": 1,
  "pageSize": 20
}
```

> 管理器**实际读取**的字段：`id`、`resourceType`、`slug`、`displayName`、`summary`、`authors[].name/nickname`、`iconUrl`、`latestVersion.versionId`。
> 其余字段可省略。

### 2.2 Mod 详情

```
GET /api/mods/{resourceType}/{slug}
```

响应：
```jsonc
{
  "id": "同上",
  "resourceType": "MOD",
  "slug": "同上",
  "displayName": "显示名称",
  "summary": "一句话简介",
  "description": "详细介绍（纯文本即可）",
  "authors": [ { "name": "作者名" } ],
  "iconUrl": "https://.../icon.png",
  "homepageUrl": "https://github.com/作者/仓库",   // 可选：填了会被用作 GitHub 更新源
  "sourceUrl": "https://github.com/作者/仓库",     // 可选
  "versions": [
    {
      "versionId": "1.2.3",
      "changelog": "更新说明",
      "publishedAt": "2026-01-02T03:04:05Z",
      "files": [
        {
          "id": "文件唯一 ID（用于取下载地址）",
          "name": "MyMod-1.2.3.zip",
          "size": 345678,
          "loaders": ["unitymodmanager"],
          "supportedGames": [ { "name": "A Dance of Fire and Ice", "version": "3.4.0" } ]
        }
      ]
    }
  ]
}
```

> 管理器**实际读取**：`versions[].versionId`、`versions[].files[].id / name / size / loaders`、`description`、`iconUrl`、`homepageUrl`、`sourceUrl`。
> **`versions` 必须包含所有历史版本**——界面上有"版本"下拉框，用户可以自己选装哪个版本。

### 2.3 获取下载地址

```
POST /api/mod-files/{fileId}/download-intent
```

响应：
```jsonc
{
  "url": "https://.../MyMod-1.2.3.zip",   // 必须能被直接 GET 下载
  "fileName": "MyMod-1.2.3.zip",
  "sourceUrl": "https://.../MyMod-1.2.3.zip"
}
```

- `url` 可以是**永久直链**，也可以是**带签名的临时地址**（管理器拿到后立刻下载）。
- 管理器会把这个 zip 直接解压安装，所以 **zip 必须是 UMM 格式**（见 §6）。
- 如果站点不需要统计下载次数，这个接口直接返回直链即可。

---

## 3. 可选接口：把 UMM 内核也放到站点上

若你的站点同时托管 **UnityModManager** 压缩包，用户在「UMM 环境 → 安装/修复加载器」里就能直接"从资源站获取"，无需手动导入。

```
GET /api/tools?search=UnityModManager&page=1&pageSize=20
GET /api/tools/{slug}
POST /api/tool-files/{fileId}/download-intent
```

`/api/tools` 响应（同 `mods` 的分页结构）：
```jsonc
{
  "items": [
    {
      "slug": "unitymodmanager",
      "displayName": "UnityModManager",
      "latestVersion": { "versionId": "0.33.0" }
    }
  ],
  "total": 1, "page": 1, "pageSize": 20
}
```

`/api/tools/{slug}` 响应：
```jsonc
{
  "slug": "unitymodmanager",
  "displayName": "UnityModManager",
  "versions": [
    { "versionId": "0.33.0", "file": { "id": "file-id", "name": "UnityModManager-0.33.0.zip", "size": 6336766 } }
  ]
}
```

要求：
- `displayName` 里要**包含 `UnityModManager`**（管理器靠这个识别）；
- 下载到的 zip 解压后，根目录（或仅一层子目录里）要有 `UnityModManager.dll` 与 `0Harmony.dll`、`dnlib.dll`、`winhttp_x64.dll`（即官方发布包的结构）。

---

## 4. 暂时未使用（预留）

以下接口管理器目前**不会调用**，实现与否不影响使用：

- `GET /api/me/mod-favorites`、`GET /api/me/mod-follows`（收藏/关注在管理器里是本地保存的）
- `GET /api/mods/{resourceType}/{slug}/comments`
- `GET /api/announcements`

---

## 5. 取值约定

**`resourceType`**（大小写不敏感）：

```
MOD | RESOURCEPACK | SHADER | PLUGIN | LIBRARY | TOOL | OTHER
```

**`loaders`**：建议至少对 UMM 的 mod 返回 `"unitymodmanager"`；管理器选中版本时会**优先挑带这个标记的文件**。

**版本号**：`versionId` 建议用 `主.次.修订`（如 `1.2.3`）。管理器按数字分段比较大小，用来判断"有没有更新"。带后缀也能识别（如 `26.5 Alpha`），但建议主体是数字。

---

## 6. Mod 压缩包要求

必须是 **UMM 格式**：zip 根目录（或唯一的一层子目录）里有 `Info.json`：

```jsonc
{
  "Id": "MyMod",                 // mod 唯一 Id（安装目录名）
  "DisplayName": "My Mod",
  "Author": "作者",
  "Version": "1.2.3",
  "ManagerVersion": "0.27.0",    // 需要的最低 UMM 版本
  "AssemblyName": "MyMod.dll",
  "EntryMethod": "MyMod.Main.Load",
  "HomePage": "https://github.com/作者/仓库",            // 可选
  "Repository": "https://raw.githubusercontent.com/.../Repository.json"  // 可选
}
```

> 💡 **强烈建议**：让 `displayName`（接口里的）与 `Info.json` 的 `Id` 保持一致。
> 管理器判断"某个在线 mod 是否已安装"时优先用这个对应关系；不一致也不会崩，但可能识别不出"已安装/可更新"。

---

## 7. 最小实现示例（Node + Express，示意）

```js
import express from 'express';
const app = express();

// 你的数据源（数据库 / JSON 文件都行）
const mods = [/* ... */];

const auth = (req, res, next) => {
  // 不校验也能用；要校验就实现这一段
  const key = (req.headers.authorization || '').replace(/^Bearer\s+/i, '');
  if (process.env.REQUIRE_KEY === '1' && key !== process.env.API_KEY) {
    return res.status(401).json({ statusCode: 401, message: '请先登录', error: 'Unauthorized' });
  }
  next();
};

app.get('/api/mods', auth, (req, res) => {
  const page = Math.max(1, parseInt(req.query.page || '1', 10));
  const pageSize = Math.min(100, Math.max(1, parseInt(req.query.pageSize || '20', 10)));
  const search = (req.query.search || '').trim().toLowerCase();
  const type = req.query.resourceType;

  let list = mods.filter(m => m.status === 'published');
  if (type) list = list.filter(m => m.resourceType === type);
  if (search) list = list.filter(m =>
    m.displayName.toLowerCase().includes(search) || (m.summary || '').toLowerCase().includes(search));

  const total = list.length;
  const items = list.slice((page - 1) * pageSize, page * pageSize).map(m => ({
    id: m.id,
    resourceType: m.resourceType,
    slug: m.slug,
    displayName: m.displayName,
    summary: m.summary,
    authors: m.authors,
    iconUrl: m.iconUrl,
    downloadCount: m.downloadCount || 0,
    latestVersion: { versionId: m.versions[0].versionId, publishedAt: m.versions[0].publishedAt },
  }));

  res.json({ items, total, page, pageSize });
});

app.get('/api/mods/:resourceType/:slug', auth, (req, res) => {
  const mod = mods.find(m => m.resourceType === req.params.resourceType && m.slug === req.params.slug);
  if (!mod) return res.status(404).json({ statusCode: 404, message: 'Not Found' });
  res.json(mod);
});

app.post('/api/mod-files/:fileId/download-intent', auth, (req, res) => {
  const file = findFileById(req.params.fileId); // 你自己的查找
  if (!file) return res.status(404).json({ statusCode: 404, message: 'Not Found' });
  res.json({ url: file.url, fileName: file.name, sourceUrl: file.url });
});

app.listen(8080);
```

不要求用任何特定技术栈——**PHP / Go / Python / 静态托管 + 云函数都能实现**。

---

## 8. 如何测试

1. 本地跑起你的接口，确认 `GET /api/mods?pageSize=5` 能返回上面格式的 JSON。
2. 打开管理器 → **设置** → 把「资源站」地址改成你的地址（`http://localhost:8080` 也可以），需要鉴权就填上 key。
3. 回到「在线 Mod」：
   - 能列出 mod = ✅ `/api/mods` 通过
   - 点一个 mod 右侧能出详情和"版本"下拉 = ✅ `/api/mods/{type}/{slug}` 通过
   - 点"安装"能装进游戏 = ✅ `/api/mod-files/{id}/download-intent` 通过

## 9. 常见坑

| 现象 | 原因 |
|---|---|
| 列表能出、点安装报错 | `download-intent` 返回的 `url` 不能直接 GET 下载（如需要 Cookie/Referer，或返回的是网页） |
| 装了但游戏里没有 | zip 不是 UMM 格式（缺 `Info.json`），或 zip 层级太深（超过一层子目录） |
| 「已安装/可更新」显示不准 | 接口的 `displayName` 与 `Info.json` 的 `Id` 不一致 |
| 检查更新很慢 | `/api/mods` 一次要返回 100 条，建议加索引/缓存 |
| 版本下拉为空 | 详情里 `versions` 为空或 `versionId` 缺失 |

---

有疑问或想联调，可把接口地址发出来一起测。
