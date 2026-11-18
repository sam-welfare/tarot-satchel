# 塔罗袋 / Tarot Satchel

为《杀戮尖塔 2》增加一个两格「塔罗牌栏位」系统：把战斗中临时生成的塔罗牌变成可收集、可携带进下一场战斗的资源。

## 玩法

- **掉落**：战斗胜利后按保底系统判定是否掉落塔罗牌（独立保底计数器：初始 15%，未掉 +10%，掉落 −20%，精英战额外 +12.5%，Boss 战与普通战相同）。掉落时一张随机塔罗牌会出现在战斗奖励界面中（卡牌形式）。
- **领取**：在奖励界面点击塔罗牌即可收进「塔罗袋」。栏位只有 2 格；满了必须先在栏位里丢弃一张，或者放弃该奖励。
- **使用 / 丢弃**：只在**战斗外**可用（战斗中禁用）。点击顶部栏药水栏下方的塔罗袋格子，弹出「使用 / 丢弃 / 取消」。
- **使用效果**：塔罗牌被消耗，并在**下一场战斗开始时**加入你的抽牌堆，正常打出。
- **一次性**：无论是否打出，这场战斗结束后它都会从牌库中移除。
- **通用**：所有角色可用；不受进阶难度影响。

## 安装（本地）

把 `TarotSatchel` 文件夹（内含 `TarotSatchel.dll` 和 `TarotSatchel.json`）放进：

```
<游戏安装目录>/mods/
```

从 Steam 启动游戏，日志出现 `RUNNING MODDED` 即表示加载成功。本 mod 修改游戏性（`affects_gameplay: true`）：使用时会写入 modded 存档目录；多人联机时所有玩家都需要安装。

## 已知限制

- 栏位满时领取奖励会被拒绝（保留奖励，可丢弃一张后再次领取），暂未做「领取时选择替换」弹窗。
- 多人联机时塔罗袋状态仅存于本地玩家档案，未做跨玩家同步（多人同装可玩，但栏位内容不同步）。
- 存档由 mod 自己维护（`%APPDATA%/SlayTheSpire2/TarotSatchel/<种子>.json`），与游戏本体存档分离。

## 开发

- 纯 C# 类库，目标框架 `net10.0`，引用游戏自带的 `GodotSharp.dll` 与 `sts2.dll`（构建时不需要安装 Godot 编辑器）。
- 入口：`[ModInitializer]` + `ModHelper.SubscribeForRunStateHooks / SubscribeForCombatStateHooks`。
- 掉落注入：`AbstractModel.TryModifyRewards`；战斗开始/结束：`BeforeCombatStart` / `AfterCombatEnd`；奖励类型：自定义 `Reward` 子类。
- 塔罗牌池：运行时 `MegaCrit.Sts2.Core.Entities.Cards.TarotCards.All`。

### 构建

```powershell
dotnet build -c Release
# 产物：TarotSatchel/bin/Release/TarotSatchel.dll
```

### 部署到游戏（本地测试）

```powershell
./scripts/deploy.ps1
```

### 调试日志

游戏日志：`%APPDATA%/SlayTheSpire2/logs/godot.log`，搜索 `[TarotSatchel]`、`[reward]`、`[combat]`、`[use]`、`[store]`。

环境变量 `TAROT_SATCHEL_TEST=1` 会启用调试模式：每次进入非战斗房间时自动使用第一格塔罗牌（用于自动化验证，仅本地调试）。
