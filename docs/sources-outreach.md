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
| **TUF**（tuforums.com） | ✅ 经超级管理员确认可用 | 未直接联系到站长；经熟悉的超管确认「应该可以」 |
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

1. `sortBy`：把「最近更新」从 `created` 改成官方已有的 **`updated`**
2. **加回链**：modlist 来源的 mod 详情里加「在 modlist.org 查看」（作者希望的回链）
3. （可选）`dependencies`：modlist 提供了依赖 slug 列表，将来可做「自动装依赖」
