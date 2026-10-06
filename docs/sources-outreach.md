# 接入第三方 mod 源 · 对外沟通稿

> 用途：联系 TUF / modlist.org 说明我们要接入他们的公开 API，征求同意。
> 原则：**只用公开、官方、文档化的接口**；不抓网页、不绕过鉴权、不转存他们的文件。

---

## 英文版（直接可发）

**Subject: Request to use your public mod catalog API in an open-source ADOFAI mod manager**

Hi there,

I maintain **ADOFAI Mod Manager (AMM)**, a free and open-source (MIT) mod manager for
*A Dance of Fire and Ice*. It replaces UnityModManager for end users: browse, install,
update and uninstall mods, plus manage the in-game loader. Repo:
https://github.com/lishangi688/adofai-mod-manager

Currently AMM reads from **ADOFAI Tools** (adofaitools.top) with its own public API, and from
GitHub release feeds. I'd like to add your site as an additional **read-only** source so our
users can find your catalog too.

**What I would like to do**

- Call your **public, documented API** — for example:
  - TUF: `GET /v2/mods`, `GET /v2/mods/{slug}`, `GET /v2/mods/{slug}/download?platform=windows`
  - modlist.org: `GET /api/mods?game=adofai`, `GET /api/mods/{slug}`, `GET /api/mods/{slug}/download?platform=windows`
- Show your entries in our “Online Mods” list, clearly **labelled with your site name**.
- Drive downloads **through your own download endpoint** so your download counters keep working
  (we follow the 302 and do not mirror or re-host any files).
- Cache our requests (we scan at most once per page and cache metadata) to keep the load light.
- Set a descriptive `User-Agent` (`AdofaiModManager/<version>`) so you can identify our traffic.

**What I will *not* do**

- No HTML scraping, no reverse-engineered/private endpoints.
- No bypassing authentication or rate limits.
- No re-hosting or re-distributing your files or metadata dumps.

**What I'd like to confirm with you**

1. Are you OK with an open-source third-party client reading your public mod API?
2. Any rate limits, caching rules, or `User-Agent`/contact requirements you'd like us to follow?
3. Would you like a link back to your site in the app (we will add it either way), or a specific
   attribution/logo?
4. Anything we should *avoid* (e.g. a preference on download counting, or a staging/beta endpoint)?
5. If you'd rather not be included, that's completely fine — just say so and I'll leave it out.

Thanks for the work you put into the ADOFAI community — happy to adapt to whatever you prefer.

Best regards,
*<your name / handle>*
Maintainer, ADOFAI Mod Manager (MIT) — https://github.com/lishangi688/adofai-mod-manager

---

## 中文摘要（自己看）

- **只用官方公开接口**（TUF 有 Swagger 文档、modlist.org 是 Nuxt 公开 JSON API），免 key。
- **不抓网页、不转存**：下载走它们自己的端点，保留它们的下载计数。
- 会**明确标注来源**、给回链、带可识别 UA、请求加缓存。
- 需要问清楚：**是否允许第三方客户端**、有无频率/UA/归因要求、有没有想让我们避开的做法。

## 沟通渠道

| 站点 | 渠道 |
|---|---|
| TUF | 站内 / Discord（其 API 文档站 `api.tuforums.com/docs/`） |
| modlist.org | 仓库 issue（github.com/modlist-org/modlist-org）或站点联系页 `/contact` |

> 备注：modlist.org 自己也有一个 mod 管理器 App（他们的版本说明里提到 “modlist.org app 0.4.3+”），
> 属于**同行**，沟通时更要礼貌、先问再做，必要时可以提“两家可以互补/互相导流”。

---

## 授权情况（2026-10-06）

| 来源 | 状态 | 说明 |
|---|---|---|
| **modlist.org** | ✅ **已获作者许可** | 作者 square3ang 回复：公开 API 可自由使用、**无需 key**；要求「下载必须走 `/download` 端点以保留计数」「请加缓存」「希望标注并回链」 |
| **TUF**（tuforums.com） | ⚠️ 待正式确认 | 使用其**公开 API**（自带 Swagger 文档，读接口免 key）；尚未取得站长的正式许可，若有渠道建议补一句确认 |
| ADOFAITools | ✅ 早已授权 | 见 README 致谢 |

### modlist.org 官方说明（要点）

- Base URL：`https://modlist.org`
- `GET /api/mods`：`game` / `categories` / `search` / `slugs` / `sortBy` / `page` / `limit`（≤100，默认 12）
  - `sortBy`：`downloads_desc`（默认）/ `downloads_asc` / `name_asc` / `name_desc` / `created` / `updated`
- `GET /api/mods/{slug}`：详情，含 `versions` / `latestVersion` / `latestBetaVersion` / `dependencies`（slug 列表）
- `GET /api/mods/{slug}/download`：302 到文件并 +1 计数；参数 `version` / `beta=true` / `platform`
- 图标：`/logos/{key}`（对应 `logo` 字段）
- **下载地址不在 JSON 里**，必须走 `/download` 端点
- 无严格频率限制，但请合理缓存
- **希望回链**：`https://modlist.org/mods/{slug}`

### 待办（对齐官方说明）

1. ✅ `sortBy`：「最近更新」已改用官方支持的 `updated`
2. ✅ **回链**：modlist 来源的 mod 详情里已显示 `https://modlist.org/mods/{slug}`（可点击）
3. （可选）`dependencies`：modlist 提供依赖 slug 列表，将来可做「自动装依赖」

---

## modlist.org 作者回复（原文存档）

**来自：square3ang（modlist.org）— 2026-10-06**

> Hi lishangi688,
>
> Thanks for asking! modlist.org has an open API, so feel free to use it in AMM. No key is needed.
>
> **Base URL:** `https://modlist.org`
>
> - **`GET /api/mods`**: list of approved mods
>   - `game`: `adofai`, `rhythm-doctor`, `dancing-line` (comma-separated)
>   - `categories`: `ui`, `gameplay`, `utility`, `visuals`, `library` (comma-separated)
>   - `search`: matches name, summary, or slug
>   - `slugs`: fetch specific mods by slug (comma-separated, max 90)
>   - `sortBy`: `downloads_desc` (default), `downloads_asc`, `name_asc`, `name_desc`, `created`, `updated`
>   - `page`, `limit` (max 100, default 12)
>   - Returns `{ mods, pagination: { total, page, limit, totalPages } }`
> - **`GET /api/mods/{slug}`**: mod details, including approved versions, `latestVersion`, `latestBetaVersion`, and `dependencies` (slugs)
> - **`GET /api/mods/{slug}/download`**: 302 redirect to the file and +1 to the download count
>   - `version`: a specific version (defaults to latest stable)
>   - `beta=true`: latest beta
>   - `platform`: `windows` / `macos` / `linux` (auto-detected from User-Agent if omitted)
>
> Logos are at `/logos/{key}` using the `logo` field.
>
> Download URLs aren't exposed in the JSON, so please always go through `/download`. That keeps the counters working, like you mentioned. No strict rate limit, just keep it reasonable with caching. A link back to the mod page (`https://modlist.org/mods/{slug}`) would be appreciated for attribution.
>
> Source is here if you need details: https://github.com/modlist-org/modlist-org
>
> Thanks!
> square3ang

**中文翻译**

> 嗨 lishangi688，
>
> 感谢你来问！modlist.org 有开放的 API，所以尽管在 AMM 里用。**不需要 key。**
>
> **Base URL：** `https://modlist.org`
>
> - **`GET /api/mods`**：已审核通过的 mod 列表
>   - `game`：`adofai` / `rhythm-doctor` / `dancing-line`（逗号分隔）
>   - `categories`：`ui` / `gameplay` / `utility` / `visuals` / `library`（逗号分隔）
>   - `search`：匹配名称、简介或 slug
>   - `slugs`：按 slug 取指定 mod（逗号分隔，最多 90 个）
>   - `sortBy`：`downloads_desc`（默认）/ `downloads_asc` / `name_asc` / `name_desc` / `created` / `updated`
>   - `page` / `limit`（最大 100，默认 12）
>   - 返回 `{ mods, pagination: { total, page, limit, totalPages } }`
> - **`GET /api/mods/{slug}`**：mod 详情，含已审核版本、`latestVersion`、`latestBetaVersion`，以及 `dependencies`（slug 列表）
> - **`GET /api/mods/{slug}/download`**：302 跳转到文件，并把下载计数 +1
>   - `version`：指定版本（默认最新稳定版）
>   - `beta=true`：最新测试版
>   - `platform`：`windows` / `macos` / `linux`（不传则按 User-Agent 自动判断）
>
> 图标在 `/logos/{key}`，用 `logo` 字段里的值。
>
> JSON 里不暴露下载地址，所以请**始终走 `/download`**，这样计数才能正常工作 —— 就跟你说的一样。没有严格的频率限制，只要合理加缓存就行。另外如果能在显眼处**回链到这个 mod 的页面**（`https://modlist.org/mods/{slug}`）就太好了，算是署名。
>
> 源码在这里，需要细节可以看：https://github.com/modlist-org/modlist-org
>
> 谢谢！
> square3ang
