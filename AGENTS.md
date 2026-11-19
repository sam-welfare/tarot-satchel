# AGENTS.md — 本仓库的硬性规则（每次开工必读）

## 0. 官方优先（用户的明确要求，最高优先级）

> **每次实现任何功能，先在官方代码/资源里找蓝图；能用官方的就用官方的，不许自己瞎搞（自绘 UI、自研机制、重复造轮子）。**

> **当官方实现与我们的需求冲突时（例：官方卡牌奖励会被某些遗物影响，而我们不希望塔罗被影响），不要自行决定取舍——用 `/grilling` 方式一次一个问题向用户确认，并给出推荐方案与取舍。**

具体到本 mod：

1. **先查再写**：动手前用 `meta_dump`（`type` / `dump` / `us` / `xref`）和游戏自带 `sts2.xml` 文档，找到官方对应的类型、钩子、奖励类型、动画/界面 API；把出处写进实现注释和 `官方化改造清单.md`。
2. **UI 一律复制官方**：底板/尺寸/间距/图标取自官方节点（如 `NPotionContainer`、`PotionBg`、`NPotionHolder`），不硬编码尺寸与坐标。
3. **奖励走官方奖励体系**：优先 `SpecialCardReward`（指定卡奖励，官方序列化）与 `SerializableRoom.ExtraRewards`；不要自建持久化。
4. **动画/反馈用官方接口**：如 `CardPileCmd.AddGeneratedCardToCombat`、`NPotionContainer.PlayAddFailedAnim` 等。
5. **只在官方确实没有对应物时**才写自定义实现，并在文档里注明「官方无对应 API，原因：…」。

## 1. 构建 / 部署 / 测试

- 构建：`cd TarotSatchel; dotnet build -c Release`
- 部署（游戏必须关闭）：复制 `TarotSatchel\bin\Release\TarotSatchel.dll` 到
  `D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\TarotSatchelFull\`
- 日志：`%APPDATA%\SlayTheSpire2\logs\godot.log`（标签 `[ui] [reward] [combat] [use] [store] [test] [persist]`)
- 自动测试：`$env:TAROT_SATCHEL_TEST='1'`；mod 自截图 `$env:TAROT_SATCHEL_SHOT=<png路径>`；探针 `$env:TAROT_SATCHEL_PROBE='1'`；栏位格数 `$env:TAROT_SATCHEL_SLOTS=<1..3>`（模拟未来栏位遗物，上限 3）

## 2. 已知的官方「蓝图」（持续补充）

| 需求 | 官方 API / 资源 | 出处 |
|---|---|---|
| 指定卡奖励 | `SpecialCardReward(CardModel, Player)`，`ToSerializable`→`SerializableReward.SpecialCard` | `MegaCrit.Sts2.Core.Rewards` |
| 奖励持久化槽位 | `SerializableRoom.ExtraRewards : Dictionary<ulong, List<SerializableReward>>` | `MegaCrit.Sts2.Core.Saves.Runs` |
| 战斗奖励注入钩子 | `AbstractModel.BeforeCombatRewardOffered(RewardsSet, CombatRoom)`；`RewardsSet.Rewards` 公开 | `MegaCrit.Sts2.Core.Models` / `.Rewards` |
| 卡牌进战斗抽牌堆 | `CardPileCmd.AddGeneratedCardToCombat(card, PileType.Draw, player, CardPilePosition.Random)` | `MegaCrit.Sts2.Core.Commands` |
| 加入牌堆的官方预览动画 | `CardCmd.Preview(card, time, CardPreviewStyle)`（「屏幕中央生成 NCard→飞向所在牌堆」），返回 TaskCompletionSource 可 await | `MegaCrit.Sts2.Core.Commands` + `sts2.xml` |
| 抽手牌前的官方钩子（动画先于抽牌） | `AbstractModel.BeforeHandDraw(Player, PlayerChoiceContext, ICombatState)`，引擎 await 后才抽牌；`BlessedAntler` 即用此钩子 | `MegaCrit.Sts2.Core.Models` |
| 药水栏底板 | `NPotionContainer` 子节点 `PotionBg`（NinePatchRect，`top_bar_char_backdrop.tres`） | 运行时探针 |
| 塔罗栏位布局（重要） | belt 面板以 `NTopBar.FloorIcon` 为轴**镜像定位**（`mirroredLeft = 2*floorCenter.X - (potions.GlobalPosition.X + potions.Size.X)`），即放在药水栏的对侧；但**槽位在面板内必须从左到右排列**（索引 0 = 最左）。曾把槽位也按面板宽度镜像（`panelW - (x + slotW)`），导致「填最左空位」实际显示在最右；面板宽度按 `Capacity` 计算，不跟随药水容器尺寸。**容量上限固定为 3**（用户实测 3 格不遮挡，决定不再增长），`SatchelStore.MaxCapacity = 3` | `SatchelBelt.EnsureAttached/UpdateGeometry` + 实测 |
| 药水栏联动 | 塔罗栏位只在**药水栏布局变化**时重新对齐：`NPotionContainer.GrowPotionHolders` postfix + `NPotionContainer.Resized` 信号 + 初始挂载；使用/丢弃药水不改变几何（空格位始终显示），因此不挂这些钩子。几何缓存 + 1px 阈值防止抖动，变化后 0.05s 再刷新等布局收敛 | `SatchelBelt.RefreshSoon/UpdateGeometry` + `PersistencePatch` |
| 药水满反馈 | `NPotionContainer.PlayAddFailedAnim()` / `PotionErrorBg` | `MegaCrit.Sts2.Core.Nodes.Potions` |
| 卡牌悬浮提示 | `NHoverTipSet.CreateAndShow(Control, IEnumerable<IHoverTip>, HoverTipAlignment)` + `HoverTipFactory.FromCardWithCardHoverTips` | `MegaCrit.Sts2.Core.Nodes.HoverTips` |
| 卡牌奖励行文案 | `LocString("gameplay_ui","COMBAT_REWARD_ADD_CARD")`；自定义描述 `SpecialCardReward.SetCustomDescriptionEncounterSource(ModelId)` | `MegaCrit.Sts2.Core.Rewards` |
| 官方 SL 语义（重要） | 官方检查点 = `RunManager.ToSave(AbstractRoom)` 返回的 `SerializableRun.PreFinishedRoom`；「保存并退出」会再调用一次 ToSave/SaveRun，但 `PreFinishedRoom` 不变（检查点未推进）；读档回到该房间开始时的状态 | `SerializableRun.PreFinishedRoom` + 实测 |
| 特殊：跨层回滚 | 打完上层 Boss、进入下一层但**还没进入「先古之民」事件**时，保存并退出会回到**上层 Boss 奖励界面**（检查点仍是 Boss 房间）；此时不能把「房间 key 变化」当成检查点推进 | 实测日志 `1:17 advanced / 3375 Creating NCombatRoom mode=FinishedCombat CEREMONIAL_BEAST_BOSS` |
| 特殊：事件选项触发战斗 | 事件里选择「进入战斗」会把战斗房压入房间栈（`BaseRoom`=事件房、`CurrentRoom`=战斗房），**这场战斗不产生检查点**；战斗结束/事件状态保存都不会推进官方检查点，可 SL 回选项界面 | `RunState.BaseRoom` 官方文档 + 实测 |
| 特殊：宝箱房/商店/火堆（空 PreFinishedRoom） | 这三类房间不实现 `ToSerializable`，`ToSave` 的 `PreFinishedRoom` 为 null；它们的官方检查点就是**房间入口**。因此在 `AfterRoomEntered` 里对 `TreasureRoom`/`MerchantRoom`/`RestSiteRoom` **立即写盘**；`ToSave` 遇到 `PreFinishedRoom == null` 一律不写（地图界面同样不写，地图结束后的保存不推进检查点） | `SatchelRunModel.AfterRoomEntered` + 实测日志 |
| 特殊：事件房间的写盘时机 | 事件房间（含先古之民）在 `AfterRoomEntered` 入口写一次；之后**只有 `PreFinishedRoom.IsPreFinished=True` 的存档**才算检查点（先古之民「拿遗物生效/选牌完成」属于这种）；`False` 的对话/战斗过程存档一律跳过；「保存并退出」的重复保存由暂停菜单补丁标记后跳过 | `SatchelRunModel.OnOfficialSnapshot` + 实测日志 |
| 特殊：事件战斗不出塔罗（当前取舍） | 事件选项触发的战斗**有**官方卡牌奖励，但它不产生检查点，无法把预掷结果固定进存档；当前选择「事件战斗不生成塔罗」（`TryModifyRewards` 找不到预掷结果就不加），以后若支持需引入 `BeforeCombatRewardOffered` 插入 + 稳定战斗 key | 用户确认的设计取舍 |
| 特殊：先古之民房间 SL | 先古之民（`EVENT.NEOW/OROBAS/PAEL` 等）只有两次有效存档：**进入房间**、**拿遗物生效**（不同遗物的存档时机不同，例如奖励选牌要选完才存）。实现：`AfterRoomEntered` 对 `EventRoom` 入口写盘；之后按 `IsPreFinished=True` 的事件存档写（不依赖遗物状态）；「保存并退出」的重复保存不写（暂停菜单 `OnSaveAndQuitButtonPressed` 标记退出流程）；读档后首次 ToSave 视为边界跳过 | `SatchelRunModel.BeginQuit/OnOfficialSnapshot` + `PersistencePatch` |
| 特殊：退出的重复保存 | 「保存并退出」是给「继续」存进度，内容仍是同一检查点，不是新检查点；退出流程内的 ToSave 不写盘 | 实测日志 + 暂停菜单补丁 |
| 掉落随机源（重要） | 塔罗掉落**不消耗官方 RNG**：`roll = Hash01(seed + "|roll|" + act:floor)`，`chance = clamp(Pity + 精英?0.125:0)`，`Pity` 初始 0.15、掉落 −0.20、未掉 +0.10；选卡 `StableHash(seed+"|card|"+key) % 22`。哈希为 FNV-1a 64 + SplitMix64 终混（必须带终混，否则相邻房间 key 输出聚集）。原因：官方 RNG 状态在 `ToSave` 内部先序列化，在 postfix 里消耗会导致读档后下一场抽到同一值 | `SatchelRunModel.PreRollRewardAtSavePoint/Hash01` |
| mod 状态写盘规则 | 只在官方检查点推进时写：`ToSave` 的 `PreFinishedRoom` 身份变化（战斗/普通房间）、或事件房间入口 + `IsPreFinished=True` 的事件存档；退出流程、读档边界、未完成事件存档一律跳过；身份随状态文件持久化（`LastCheckpoint`），读档后首次 ToSave 自动跳过 | `SatchelRunModel.OnOfficialSnapshot` + `PersistencePatch`（共 3 个补丁：Reward.FromSerializable、RunManager.ToSave、NPauseMenu.OnSaveAndQuitButtonPressed） |

## 3. 临时文件规则

- **所有临时文件（截图、日志导出、探针输出、分析脚本产物等）一律放进仓库根的 `temp/` 目录**，不要留在根目录或源码目录。
- **每次对话开始时先遍历 `temp/`**，把已经没用的（旧的连拍帧、过时的截图、一次性导出）删掉；只保留仍然有参考价值的证据文件。
- `temp/` 下的文件不会随 mod 发布，也不会被 `dotnet build` 打包。

## 4. 禁止事项

- 不要用 PowerShell 对含中文的 `.cs` 做 `-replace`/`Set-Content`（会把中文写成乱码）；一律用编辑工具改。
- 不要在游戏运行时覆盖 DLL（会失败）；先确认进程已退出。

## 5. Git / GitHub 托管

- 仓库根已含 `.gitignore` / `.gitattributes`：只跟踪源码、脚本、文档与 `workshop/` 发布文件。
- **不提交**：`**/bin`、`**/obj`、`temp/`、`api_dump/`、`pck_dump/`、构建产物 DLL（发布走 Releases）。
- 初始化：`pwsh -File scripts/git-setup.ps1`；配置远端：`git remote add origin <repo-url>`、`git push -u origin main`。
- 提交信息前缀：`feat:` / `fix:` / `docs:` / `chore:` / `refactor:`。
- 构建与部署/测试仍在本机 Windows 完成（见 §1），git 只负责版本与远端同步。

