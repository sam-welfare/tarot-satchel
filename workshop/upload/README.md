# 创意工坊发布（官方 ModUploader）

官方上传器：<https://github.com/megacrit/sts2-mod-uploader/releases>（`ModUploader.exe`）

## 首次发布

1. 构建并生成工作区（在仓库根目录）：
   ```powershell
   cd TarotSatchel; dotnet build -c Release; cd ..
   pwsh -File scripts/prepare-workshop.ps1
   ```
   生成 `workshop/upload/TarotSatchel/`：
   - `Content/TarotSatchel.dll`、`Content/TarotSatchel.json`
   - `image.png`（来自 `workshop/icon.png`，必须 <1MB）
   - `workshop.json`（模板）
2. 编辑 `workshop/upload/TarotSatchel/workshop.json`，**至少填 `tags`**（发布后无法再改）。
   建议：`"tags": ["Cards", "QoL", "schinese", "English"]`（按实际工坊标签核对）。
   其他字段（title/description/visibility/changeNote）官方建议留 `null`，上传后在工坊页面手填，
   避免以后更新覆盖页面内容。
3. 把 `ModUploader.exe` 放到 `workshop/upload/`，在该目录打开终端：
   ```powershell
   .\ModUploader.exe upload -w TarotSatchel
   ```
4. 上传成功后工作区会生成 `mod_id.txt`（工坊物品 ID）。把它复制到 `workshop/mod_id.txt` 并提交；
   `scripts/prepare-workshop.ps1` 重建工作区时会自动把它拷回去。
5. 在工坊页面填写：
   - 标题：`Tarot Satchel (塔罗袋)`
   - 简介：复制 `workshop/description_eng.txt` + `workshop/description_zhs.txt`
   - 可见性：public
   标签在 json 里已定，页面不可改。

## 更新已发布版本

```powershell
pwsh -File scripts/prepare-workshop.ps1      # 覆盖 Content 与 image.png，保留 workshop.json/mod_id.txt
cd workshop\upload
.\ModUploader.exe upload -w TarotSatchel
```

可在 `workshop.json` 的 `changeNote` 写本次更新说明（注意它会覆盖工坊页面上的更新说明）。

## 注意

- `workshop.json` 中的非 null 字段每次上传都会覆盖工坊页面内容。
- `image.png` >1MB 会上传失败。
- 工坊标签一旦发布不可修改。
