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

## 最小可用流程

每個 mod 都照這個順序處理：

1. clone 上游 repo
2. checkout 指定 `commit` 或 `tag`
3. update submodules
4. 設定 localizer 環境變數
5. 跑翻譯器
6. 準備 Dalamud 編譯依賴
7. `dotnet build`
8. 打包 zip artifact

如果順序打亂，通常會在版本解析或缺 DLL 時爆掉。

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
