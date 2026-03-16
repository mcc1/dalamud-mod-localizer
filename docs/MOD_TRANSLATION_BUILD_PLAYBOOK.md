# Generic Mod Localizer Framework

這份文件不是經驗摘要，而是把目前這個專案整理成「可套到其他 Dalamud mod」的實作說明。

目標是：

1. 給定上游 repo 與 commit/tag。
2. 套用同一套 Roslyn 翻譯器。
3. 準備台服可用的 Dalamud 編譯依賴。
4. 產出可下載的 `ModName.zip` artifact。

## 這個框架現在支援什麼

目前 `DalamudModLocalizer.csproj` 提供的是一個可配置的翻譯器執行入口：

- `Program.cs`
- `TranslationRewriter.cs`

它不再綁死 `AutoRetainer`，而是透過環境變數決定：

- 要翻譯哪個 repo 目錄
- 要掃哪些 source 目錄
- 要用哪個字典檔

## 可配置參數

`dotnet run --project DalamudModLocalizer.csproj` 會讀這些環境變數：

- `LOCALIZER_REPO_DIR`
  預設值：`AutoRetainer`
  說明：上游 mod clone 到本地後的資料夾名稱。

- `LOCALIZER_SOURCE_SUBPATHS`
  預設值：`AutoRetainer`
  說明：要翻譯的 source 目錄，可用 `;` 分隔多個路徑。
  這些路徑是相對於 `LOCALIZER_REPO_DIR`。

- `LOCALIZER_DICT_PATH`
  預設值：`zh-TW.json`
  說明：翻譯字典檔路徑，可放在 repo 根目錄，也可給絕對路徑。

## 執行模式

共用 workflow 現在分成 3 種模式：

1. `extract`
2. `build`
3. `sync`

它們的用途不同，不要混用：

- `extract`
  - 使用 consumer repo 目前已有的 source snapshot
  - 只跑 localizer，補漏字串到 `zh-TW.json`
  - 不 build、不打包、不上傳 artifact
- `build`
  - 使用 consumer repo 目前已有的 source snapshot
  - 跑 localizer、套 patch、build、package
  - 不重新抓 upstream，也不回寫 source
- `sync`
  - 升版用
  - 重新 clone pinned upstream
  - 跑 localizer、套 patch、build、package
  - 視設定決定是否 commit 產生出的 source snapshot

對台版/陸版這種長時間停在同一個上游版本的情境：

- 平常用 `extract` 或 `build`
- 只有大版本更新時才用 `sync`

## Consumer 翻譯硬規則

下面這幾條不是建議，而是 consumer 維護時的硬規則：

1. 不可以直接改 committed source 然後把修改只留在 source snapshot。
2. 能靠 `extract`、字典、shared localizer 解決的翻譯，不可以先改 source。
3. 只有在下列情況才可以動 source：
   - 功能修正
   - localizer 目前抓不到的 UI 字串形狀
   - 第三方共用 UI 元件的硬編碼提示字
4. 只要翻譯修改必須直接動 source，就必須立刻整理成 consumer patch，不能只存在於當前 snapshot。
5. 每一個「因翻譯而需要 source patch」的修改，都必須同步記錄下來。

必記錄內容至少包含：

- 為什麼字典 / localizer 不足以處理
- 實際修改了哪些檔案
- 修的是哪個畫面、下拉選單、tooltip、按鈕或提示字
- 這個 patch 是長期保留，還是未來應回推到 shared localizer

如果沒有做到上面兩件事：

- 沒轉成 `.consumer-patches/`
- 沒寫進維護記錄

那就不能算完成。因為下一次 `sync` 或升版時，這些修改會被 workflow 蓋掉。

## 最小可用流程

### `extract`

1. 使用 consumer repo 現有 source
2. 設定 localizer 環境變數
3. 跑翻譯器
4. 更新字典

### `build`

1. 使用 consumer repo 現有 source
2. 設定 localizer 環境變數
3. 跑翻譯器
4. 將所有必要 source 修正維持在 consumer patch 中
5. 準備 Dalamud 編譯依賴
6. `dotnet build`
7. 打包 zip artifact

### `sync`

1. clone 上游 repo
2. checkout 指定 `commit` 或 `tag`
3. update submodules
4. 設定 localizer 環境變數
5. 跑翻譯器
6. 套 consumer patch
7. 準備 Dalamud 編譯依賴
8. `dotnet build`
9. 打包 zip artifact
10. 視需要 commit source snapshot

注意：

- `sync` 會重建 source snapshot
- 所有沒有進 patch 層的 direct source translation changes 都會消失
- 所以任何翻譯用 source 修正，都必須先 patch 化再談升版

## 你要替換的只有這些東西

把 AutoRetainer 換成其他 mod 時，真正要改的只有下面這些欄位：

1. 上游 repo URL
2. 上游固定版本 `MOD_REF`
3. clone 後的資料夾名
4. `LOCALIZER_SOURCE_SUBPATHS`
5. 字典檔名，例如 `Lifestream.zh-TW.json`
6. build project 路徑
7. artifact 名稱
8. `required_files` 清單
9. `DALAMUD_ASSET_URL`
10. `dotnet_sdk_versions`

如果 consumer repo 還要把 `repo.json` 同步到 GitLab plugin feed，另外還要記得：

1. 在 consumer repo 的 GitHub Actions secrets 新增 `GITLAB_PUSH_TOKEN`
2. token 必須能存取目標 GitLab repo
3. token 至少要有 repository read/write 權限

少了這個 token，不會影響 GitHub 上的 build 或 release，但最後的 GitLab sync 會直接失敗，常見訊息是 `HTTP Basic: Access denied`。

## 為什麼一定要 pin 版本

這個框架的前提不是「永遠抓最新」，而是「翻譯與建置都建立在可重現版本上」。

你至少要 pin：

- `MOD_REF`
- `DALAMUD_ASSET_URL`
- `DALAMUD_ASSETS_JSON_URL`

否則今天能編，明天可能因為：

- API12 / API13 混用
- .NET 版本升級
- yanmucorp release 漂移
- 子模組 hash 改變

直接失敗。

## API 判斷規則

先確認目標 mod 屬於哪一代 Dalamud API：

- API12：常見引用 `ImGui.NET`
- API13+：常見引用 `Dalamud.Bindings.ImGui`、`Dalamud.Bindings.ImPlot`、`Dalamud.Bindings.ImGuizmo`

判斷方式：

```bash
grep -RInE 'ImGuiNET|Dalamud\\.Bindings' <repo>
```

這一步很重要，因為 `required_files` 和 `DALAMUD_ASSET_URL` 都要跟著變。

## 通用 workflow 模板

下面是可直接改名套用的骨架。重點不是檔名，而是變數位置。

```yaml
name: Build Translated Mod

on:
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest
    env:
      MOD_NAME: Lifestream
      MOD_REPO_URL: https://github.com/NightmareXIV/Lifestream.git
      MOD_REF: <commit-or-tag>
      MOD_DIR: Lifestream
      MOD_PROJECT: Lifestream/Lifestream/Lifestream.csproj
      LOCALIZER_REPO_DIR: Lifestream
      LOCALIZER_SOURCE_SUBPATHS: Lifestream/UI;Lifestream/Windows
      LOCALIZER_DICT_PATH: Lifestream.zh-TW.json
      DALAMUD_ASSET_URL: <pinned-yanmucorp-asset-url>
      DALAMUD_ASSETS_JSON_URL: https://raw.githubusercontent.com/yanmucorp/DalamudAssets/master/assetCN.json

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            9.0.x

      - name: Workflow Mode
        run: echo "workflow_mode=sync"

      - name: Clone Mod
        run: |
          rm -rf "$MOD_DIR"
          git clone "$MOD_REPO_URL" "$MOD_DIR"
          if ! git -C "$MOD_DIR" rev-parse --verify "$MOD_REF^{commit}" >/dev/null 2>&1; then
            git -C "$MOD_DIR" fetch origin "$MOD_REF"
          fi
          git -C "$MOD_DIR" checkout --detach "$MOD_REF"
          git -C "$MOD_DIR" submodule sync --recursive
          git -C "$MOD_DIR" submodule update --init --recursive

      - name: Run Translation
        run: |
          dotnet run --project DalamudModLocalizer.csproj

      - name: Prepare Dalamud Dependencies
        run: |
          runner_tmp="${RUNNER_TEMP:-/tmp}"
          mkdir -p "$runner_tmp/dalamud"
          curl -fL --retry 3 "$DALAMUD_ASSET_URL" -o "$runner_tmp/dalamud/archive.7z"
          if command -v 7z >/dev/null 2>&1; then
            7z x -y "$runner_tmp/dalamud/archive.7z" "-o$runner_tmp/dalamud"
          elif command -v bsdtar >/dev/null 2>&1; then
            bsdtar -xf "$runner_tmp/dalamud/archive.7z" -C "$runner_tmp/dalamud"
          else
            sudo apt-get update
            sudo apt-get install -y p7zip-full
            7z x -y "$runner_tmp/dalamud/archive.7z" "-o$runner_tmp/dalamud"
          fi
          curl -fL --retry 3 "$DALAMUD_ASSETS_JSON_URL" -o "$runner_tmp/dalamud/assetCN.json"
          jq -r '.Assets[] | [.Url, .FileName] | @tsv' "$runner_tmp/dalamud/assetCN.json" | while IFS=$'\t' read -r url file_name; do
            [ -z "$url" ] && continue
            [ -z "$file_name" ] && continue
            out_path="$runner_tmp/dalamud/$file_name"
            mkdir -p "$(dirname "$out_path")"
            curl -fL --retry 3 "$url" -o "$out_path"
          done
          echo "DALAMUD_HOME=$runner_tmp/dalamud/" >> "$GITHUB_ENV"

      - name: Build
        run: |
          dotnet build "$MOD_PROJECT" -c Release -p:CustomCS=true -p:EnableWindowsTargeting=true
```

## `required_files` 怎麼訂

這不是固定值，要看 API 世代。

API12 常見：

- `Dalamud.dll`
- `Dalamud.Common.dll`
- `ImGui.NET.dll`
- `Lumina.dll`
- `Lumina.Excel.dll`
- `ImGuiScene.dll`
- `InteropGenerator.Runtime.dll`
- `FFXIVClientStructs.dll`

API13+ 常見：

- `Dalamud.dll`
- `Dalamud.Common.dll`
- `Dalamud.Bindings.ImGui.dll`
- `Dalamud.Bindings.ImPlot.dll`
- `Dalamud.Bindings.ImGuizmo.dll`
- `Lumina.dll`
- `Lumina.Excel.dll`
- `ImGuiScene.dll`
- `InteropGenerator.Runtime.dll`
- `FFXIVClientStructs.dll`

如果 build error 說 namespace 不存在，先回頭確認 API 世代，不要盲改 code。

## .NET SDK 相容策略

reusable workflow 不再把 SDK 安裝版本寫死在模板裡。

現在 consumer repo 可以透過 `dotnet_sdk_versions` 指定要安裝哪些 SDK，例如：

```yaml
      dotnet_sdk_versions: |
        8.0.x
        9.0.x
```

如果之後 API14 線需要 .NET 10，可以直接改成：

```yaml
      dotnet_sdk_versions: |
        8.0.x
        9.0.x
        10.0.x
```

原則：

- localizer 本身目前仍是 `net8.0`
- consumer mod 要幾版 SDK，就在 consumer workflow 明確列出
- 不要等到升 API 才去改 shared template 的寫死版本

## Lifestream 套用方式

如果你要把這套框架移植到 `Lifestream`，先做這幾件事：

1. 找到 Lifestream 對台服可用的 commit/tag
2. 掃描它是 API12 還是 API13+
3. 確定 `.csproj` 的 `TargetFramework`
4. 設定下面這組變數

範例：

```bash
export LOCALIZER_REPO_DIR=Lifestream
export LOCALIZER_SOURCE_SUBPATHS='Lifestream/UI;Lifestream/Windows;Lifestream'
export LOCALIZER_DICT_PATH=Lifestream.zh-TW.json
dotnet run --project DalamudModLocalizer.csproj
```

然後在 workflow 中對應：

- `MOD_DIR=Lifestream`
- `MOD_PROJECT=Lifestream/Lifestream/Lifestream.csproj`
- `MOD_REF=<pinned-commit>`

## 這個框架的限制

它目前適合：

- UI 文字本地化
- 字串字典累積
- 固定版本編譯
- Dalamud plugin 類型的 repo

它目前不自動處理：

- 特殊 codegen 流程
- 非標準 build script
- 需要私有 feed / 私有 submodule 的 repo
- 過度依賴 source generator 的客製框架

遇到這類 mod，還是要另外補 build-specific step。

## 翻譯器目前的安全保證

`TranslationRewriter.cs` 目前已修正一般插值字串 escaping，會正確轉義：

- `\\`
- `\"`
- `\r`
- `\n`
- `\t`

這是為了避免翻譯後直接把換行寫進 C# 字串，造成：

- `CS1039 Unterminated string literal`
- `CS1010 Newline in constant`

## 最小驗證清單

每次換到新 mod，至少做這 6 個檢查：

1. `MOD_REF` 對應 commit/tag 真存在。
2. submodule 能完整拉下來。
3. localizer 能找到 `LOCALIZER_SOURCE_SUBPATHS` 指向的目錄。
4. `required_files` 全存在。
5. `dotnet build` 0 error。
6. artifact 只產出一個乾淨的 `ModName.zip`。

## Patch 工作流修正

這一段是硬規則，不是建議。

不要再用這種順序：

1. 直接改 consumer repo 裡的 source snapshot
2. 覺得功能對了之後才回頭抽 patch
3. 最後才發現 `sync` / `build` 的 patch base 根本不是那個 snapshot

這樣很容易出現：

- patch 套不上
- patch 疊太多層
- 同一功能線互相覆蓋
- 一直浪費 GitHub Actions run 才發現 base 錯了

正確順序：

1. 先判斷這次要對齊的是 `sync`、`build`、還是 `extract`
2. 用對應模式重現 patch base
3. 只在那個 base 上做修改
4. 先本地驗證 patch
5. 通過後才推送並跑 workflow

### Patch Base 定義

- `sync`
  - `clone pinned upstream`
  - `Run Localizer`
  - 這個狀態才是 patch base
- `build`
  - `consumer repo current source snapshot`
  - `Run Localizer`
  - 這個狀態才是 patch base
- `extract`
  - 通常不應該新增 consumer patch

### 本地驗證指令

框架現在提供：

- `scripts/validate_consumer_patches.py`

用途：

- 在暫存目錄重現 workflow 到 `Apply Consumer Patches` 前一刻
- 依序檢查 patch 是否能套用
- 提前抓出 base 錯誤與 patch 重疊問題

範例：

```bash
python scripts/validate_consumer_patches.py \
  --consumer-repo /path/to/Lifestream-zhTW \
  --workflow-mode sync \
  --mod-repo-url https://github.com/NightmareXIV/Lifestream.git \
  --mod-ref e91124d8f7fb0477b46d2c12c9db0fd59e66f3ad \
  --mod-repo-dir Lifestream \
  --localizer-source-subpaths Lifestream \
  --localizer-dict-path zh-TW.json
```

如果只是日常驗證 consumer repo 目前 snapshot：

```bash
python scripts/validate_consumer_patches.py \
  --consumer-repo /path/to/Lifestream-zhTW \
  --workflow-mode build \
  --mod-repo-dir Lifestream \
  --localizer-source-subpaths Lifestream \
  --localizer-dict-path zh-TW.json
```

### Patch 收斂規則

如果幾個 patch 都在修同一條功能線，預設要合併，不要往上疊。

例如：

- 台服 cross-world 名稱別名
- 台服 world list
- 點選 world 後的解析

這些如果都在修「台服跨服旅行」，就應該盡量收成同一個 patch，而不是拆成三層彼此相依。

## 建議的專案結構

如果你真的要把這個 repo 長期拿來套多個 mod，建議最終整理成：

- `Program.cs`
  通用入口
- `TranslationRewriter.cs`
  通用翻譯器
- `docs/`
  每個 mod 的 build note
- `<ModName>.zh-TW.json`
  每個 mod 各自字典
- `.github/workflows/<mod-name>.yml`
  每個 mod 各自 workflow

這樣才不會把 AutoRetainer 與 Lifestream 的配置混在一起。

## 結論

要把這套框架用到其他 mod，不需要重寫 localizer。你只需要：

1. pin 正確的 mod commit
2. pin 正確的 Dalamud asset
3. 設定 `LOCALIZER_REPO_DIR`
4. 設定 `LOCALIZER_SOURCE_SUBPATHS`
5. 設定 `LOCALIZER_DICT_PATH`
6. 換掉 build project 路徑與 artifact 名稱

剩下的流程和 AutoRetainer 是同一套。
