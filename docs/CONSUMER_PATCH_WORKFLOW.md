# Consumer Patch Workflow

這份文件只處理一件事：

- 如何正確修改 consumer patch
- 如何在本地驗證 patch base
- 如何避免 patch 疊太多層

## 先記住一句話

不要先改 committed source snapshot，再回頭抽 patch。

`sync` / `build` 的 patch base 都不是「你現在 repo 裡看到的 source snapshot 本身」，而是 workflow 在當下跑到 `Apply Consumer Patches` 前一刻的狀態。

## Patch Base

### `sync`

patch base 是：

1. clone pinned upstream
2. checkout pinned ref
3. update submodules
4. run localizer

### `build`

patch base 是：

1. consumer repo 目前已有 source snapshot
2. run localizer

### `run_localizer=false`

patch base 是：

1. workflow source state
2. 不跑 localizer

這種情況本地驗證要加 `--skip-localizer`。

## 本地驗證指令

### `sync`

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

### `build`

```bash
python scripts/validate_consumer_patches.py \
  --consumer-repo /path/to/Lifestream-zhTW \
  --workflow-mode build \
  --mod-repo-dir Lifestream \
  --localizer-source-subpaths Lifestream \
  --localizer-dict-path zh-TW.json
```

### `run_localizer=false`

```bash
python scripts/validate_consumer_patches.py \
  --consumer-repo /path/to/SomeConsumer \
  --workflow-mode build \
  --mod-repo-dir SomeMod \
  --localizer-source-subpaths SomeMod \
  --localizer-dict-path zh-TW.json \
  --skip-localizer
```

## 正確修改順序

1. 先決定這次問題屬於 `sync` 還是 `build`
2. 跑 `validate_consumer_patches.py`，先確認現在 patch 結構是乾淨的
3. 在保留的暫存 workspace 或等價 base 上做修改
4. 只把修改回收成 patch，不直接把 source snapshot 當最終變更
5. 再跑一次本地驗證
6. 最後才推送並跑 GitHub Actions

## Patch 收斂規則

同一功能線盡量只留一層 patch。

例如這些應該收斂成同一個 patch：

- 台服 world 名稱別名
- 台服 world list
- 點 world 後的解析

這些如果都在修「台服跨服旅行」，不要拆成多層相依 patch。

## 什麼情況該拆 patch

只有在這些情況才值得分開：

- 完全不同的功能線
- 不同檔案群組、不同維護邏輯
- 之後預期會獨立回退

例如：

- CI 路徑修正
- API12 相容修正
- 台服跨服旅行修正

這三種分開是合理的。

## 失敗時先看什麼

如果 workflow 顯示：

- `Patch could not be applied cleanly`

先不要直接改 source snapshot。先做這兩步：

1. 跑本地 validator
2. 確認現在 patch 是不是對錯 base 生出來的

如果 validator 失敗，它會保留暫存 workspace，直接去看那份 workspace 才是正確方向。
