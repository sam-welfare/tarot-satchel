# 塔罗袋 / Tarot Satchel — 开发状态

> 目标：STS2 mod「塔罗袋」，2 格塔罗栏位；战斗外使用/丢弃；使用后下一场战斗入抽牌堆、战斗后移除；掉落保底（15% 起，未掉 +10/掉落 −20，精英 +12.5%）；卡牌形式奖励；满仓可弃旧或放弃；纯 C#；先本地验证再上创意工坊。

## 已验证可用（游戏日志证据）

- [x] 纯 C# mod 加载：`Loaded 6 mods`、`[TarotSatchel] initializing`、`Finished mod initialization`
- [x] 掉落保底滚动与日志：`[reward] Monster no drop roll=… chance=… pityAfter=…` / `drop -> CARD.…`
- [x] 掉落以卡牌形式进入奖励界面（修复了掉落分支 NRE）
- [x] 领取入库：`[reward] claimed tarot CARD.JUSTICE; slots=[CARD.JUSTICE]`
- [x] 栏位 UI 挂载到顶栏 Boss 图标与计时器之间：`[ui] belt attached between boss icon and timer (container)`
- [x] 栏位实时渲染已入库的塔罗：`[ui] render slots=[CARD.THE_STAR] netId=1 pity=0.45`
- [x] 点格子弹原生菜单（`PopupMenu`）：`[ui] slot 0 pressed (魔术师, combat=False); menu at (1298,38)` → `[ui] menu id=1 slot=0`
- [x] 使用后**不入牌库**，只登记队列：`[use] … queued for next combat; … deck=11`（牌库数量不变）
- [x] 进入下一场战斗时注入抽牌堆：`[combat] injected CARD.THE_WORLD into draw pile (pile=12)` → `queuedInDrawPile=1`
- [x] 战斗结束后清理队列：`[combat] queued tarot CARD.THE_WORLD finished its combat`
- [x] 丢弃（menu id=2）验证通过：`[ui] menu id=2 slot=0` → 栏位清空、不产生队列
- [x] 模型注册 ModelId 后消除 `RunState.Contains` 空引用（牌组视图探针下 NRE 计数 0）
- [x] **真人实测通过（2026-11-15 11:46）**：掉落、卡面、领取入库、栏位显示、点格子菜单、使用（只进下一场抽牌堆、不进牌库）、丢弃 —— 用户确认「基本没问题」
- [x] 游戏升级到 **v0.113.1** 后 mod 正常加载（`MODDED (6)`）；兼容修复：模型 Id 延迟初始化、描述格式化改用 raw text
- [x] **新 UI 重构（2026-11-16）**，自动化验证通过（三轮）：
  - 卡牌展示层拾取：`[test] picked offered tarot card` → `claimed tarot …`
  - 栏位渲染/悬浮：`[ui] render slots=[CARD.THE_TOWER] combat=False`、`[ui] hover slot 0 -> CARD.THE_TOWER`（官方 NHoverTipSet）
  - 使用仍不入牌库：`[use] … deck=11`
  - **官方动画注入**：`[combat] animated add of … success=True`（`CardPileCmd.AddGeneratedCardToCombat`）
  - 下场抽牌堆确认：`queuedInDrawPile=1`；战后清理：`finished its combat`
- [ ] 视觉验收（待用户目视）：栏位镜像位置/尺寸、灰彩卡牌图标、悬浮卡面预览、药水式弹窗、满仓红闪抖动+跳过
- [x] 奖励界面（战斗已结束）允许使用/丢弃：实时战斗判定改为「敌人存活」，见 `CombatProbe`
- [x] 满仓领取：弹出原生选择菜单「替换某张 / 放弃」（待真人目视确认）
- [x] 自动化测试驱动（`TAROT_SATCHEL_TEST=1`）：自动点菜单开局、`kill all` 秒杀、点击奖励、走真实 UI 路径使用

## 本轮修复的根因

1. `RunModel.RunState` 基类属性赋值被游戏拒绝（`Canonical model … used in incorrect place`）→ 改为模型自持 `_state` 字段，`CurrentRun => _state`。
2. 栏位渲染空转：依赖上面的 `CurrentRun`；修复后渲染正常。
3. 战斗级钩子（`BeforeCombatStart`/`AfterCombatEnd`/`AfterRoomEntered`）不会送达 mod 订阅模型 → 用 1s 轮询检测战斗进出。
4. 创建塔罗卡实例：用 `RunState.CreateCard(canonical, player)` 后直接 `player.Deck.AddInternal(instance, -1, true)`（`AddCard` 会因卡已有 owner 报错）。
5. 掉落分支 NRE：`NextItem`/卡名取值加防护，`TryModifyRewards` 全程 try/catch。
6. 卡名显示：`CardModel.TitleLocString` 对塔罗为空；用 `<ENTRY>.title` 本地化键（`TarotNames`，回退表 `cards/card/card_keywords/keywords/eng`）。
7. 弹窗被顶栏裁剪/被游戏高层 CanvasLayer 盖住：改用 Godot 原生 `PopupMenu`（Window 级，永远在最上层且自己处理输入）。
8. 奖励界面里 `CurrentRoom` 仍是 `CombatRoom`，导致「战斗中」判定把使用/丢弃禁用：改用 `CombatProbe.IsInLiveCombat`（看敌人是否存活）。
9. 满仓领取：`TarotSatchelReward.OnSelect` 走 `ChoicePopup` 原生菜单，支持替换或放弃。
10. **使用不应入持久牌库**：改为「使用仅登记队列 → 战斗开始时 `CombatState.CreateCard` + `CardPile.Get(Draw).AddInternal` 注入抽牌堆 → 战斗结束清队列」；牌库数量全程不变。
11. 模型未注册 `ModelId` 会导致 `RunState.Contains` 空引用（BaseLib 迭代钩子时触发）：`Model.InitId(new ModelId("run_model","tarot_satchel"))` 后消除。


## 工程结构

```
TarotSatchel/                 # mod 工程（纯 C#，net10.0，引用游戏 GodotSharp.dll + sts2.dll）
  ModEntry.cs                 # [ModInitializer]，订阅 run/combat hooks，启动轮询与测试驱动
  SatchelRunModel.cs          # 掉落保底、奖励注入、使用/丢弃、战斗后移除、自持 RunState
  SatchelStore.cs             # 玩家级状态（保底值/栏位/待用队列）持久化到 %APPDATA%/SlayTheSpire2/TarotSatchel/<seed>.json
  TarotSatchelReward.cs       # 自定义 Reward（卡面图标 + 卡名 + 领取入库）
  TarotNames.cs               # 塔罗卡显示名/描述解析（本地化表回退）
  SatchelBelt.cs              # 顶栏 2 格 UI（Boss 图标与计时器之间）+ 使用/丢弃弹窗
  SatchelPoller.cs            # 1s 轮询：战斗进出、栏位刷新（战斗钩子不送达订阅模型）
  TestDriver.cs               # 仅 TAROT_SATCHEL_TEST=1：自动开局/杀怪/领奖/自动使用
  TarotSatchel.json           # manifest（id/name/version/has_dll/affects_gameplay/min_game_version）
scripts/deploy.ps1            # 构建产物部署到游戏 mods 目录
scripts/make_icon.py          # 生成创意工坊图标
workshop/icon.png             # 512x512 图标
workshop/description_{zhs,eng}.txt
api_dump/ meta_dump/ pck_dump/ sim_pity.py  # 逆向/模拟辅助工具（非发布内容）
```

## 关键技术结论

- 官方钩子：`ModHelper.SubscribeForRunStateHooks` 的模型能收到 **run 级钩子**（`TryModifyRewards` 实测有效）；
  但 `BeforeCombatStart` / `AfterCombatEnd` / `AfterRoomEntered` 等**战斗/房间钩子不会送达**，需用 1s 轮询或 Harmony。
- 塔罗牌池：运行时 `MegaCrit.Sts2.Core.Entities.Cards.TarotCards.All`（22 张，`ModelId` 形如 `CARD.THE_HIGH_PRIESTESS`）。
- 卡名：`CardModel.TitleLocString` 对塔罗牌为空；本地化键为 `<ENTRY>.title`（如 `THE_HIGH_PRIESTESS.title`），
  表名回退顺序 `cards → card → card_keywords → keywords → eng`（`TarotNames` 内实现，成功时打 `[names]` 日志）。
- 奖励 UI：自定义 `Reward` 子类，`CreateIcon()` 返回 `NCard.Create(card, ModelVisibility.Visible)` 缩放 0.32。
- manifest JSON 用纯 ASCII；含中文名会被游戏按 GBK 解码成乱码。
- mod 加载器递归扫描 mods 目录，`*.disabled` 改文件夹名并不会禁用（需移除 manifest 或移出目录）。
- 自动测试：`SlayTheSpire2.exe -- --autoslay …` 未触发；改为 mod 内置 `TestDriver`（`TAROT_SATCHEL_TEST=1`）驱动 UI/控制台。

## 常用命令

```powershell
# 构建
cd TarotSatchel; dotnet build -c Release
# 部署
powershell -File scripts/deploy.ps1
# 自动化验证（会在开局后自动杀怪/领奖/使用）
$env:TAROT_SATCHEL_TEST='1'; Start-Process 'D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe'
# 日志
Select-String "$env:APPDATA\SlayTheSpire2\logs\godot.log" -Pattern '\[test\]|\[ui\]|\[reward\]|\[combat\]|\[use\]'
```

## 下一轮待办（2026-11-16 01:30 用户验收反馈：全官方重构）

1. 存档退出后塔罗奖励消失/栏位错位 -> 奖励改为官方 CardReward 子类（官方序列化恢复），OnSelect 拦截搬进栏位。
2. 使用后飞入动画不可见 -> 官方 CreateCardNodeAndUpdateVisuals + AddGeneratedCardToCombat，注入时机延后到战斗 UI 就绪。
3. 卡牌奖励显示错位/尺寸/文案错 -> 直接用官方卡牌奖励选择界面，弃用自绘 NCard 容器。
4. UI 非官方 -> 栏位改为去脚本克隆官方药水栏容器（Duplicate + 递归 SetScript(null)），空/满槽用官方卡牌图标灰/彩两态。
5. 已修并部署：栏位不随药水栏变动 -> 每次刷新重算镜像位置/尺寸/槽位偏移（待目视确认）。
6. 视觉验收 + 创意工坊发布 + 清理遗留。

## 下一轮参考文档

- 完整需求对照与官方素材用法见 [官方化改造清单.md](官方化改造清单.md)（含：遗物隔离、官方 CardReward、去脚本克隆药水栏、官方悬浮提示、飞入动画、满仓反馈、持久化修复）。
