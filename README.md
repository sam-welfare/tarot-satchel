# 塔罗袋 / Tarot Satchel

为《杀戮尖塔 2》增加一个常驻的「塔罗袋」：战斗掉落的塔罗牌可以收进袋中，在战斗外使用，并在**下一场战斗**的抽牌堆里打出，用后即弃。

- **Steam 创意工坊**：<https://steamcommunity.com/sharedfiles/filedetails/?id=3843479397>
- **版本支持**：public-beta 测试分支 v0.113
- **无前置依赖**
- **协议**：代码采用 [PolyForm Noncommercial 1.0.0](LICENSE)，**禁止任何商业用途**；游戏素材（见下方「素材说明」）不在本协议范围内。

## 玩法

- **掉落**：战斗胜利后按保底机制判定：初始 15%，未掉落时下次概率 +10%，掉落后 −20%，精英 +12.5%；Boss 战与普通战斗掉落率相同。
- **领取**：掉落的塔罗牌以卡牌奖励形式出现在战斗奖励界面，点击后收进顶部栏的「塔罗袋」。
- **栏位**：塔罗袋位于药水栏对侧，默认 2 格，和药水栏一样常驻显示；栏位满时拒绝领取并播放满仓反馈。
- **使用 / 丢弃**：只在战斗外可用；点击塔罗牌图标弹出「使用 / 丢弃」。战斗中不可操作。
- **效果**：使用后，该塔罗牌在下一场战斗开始时加入抽牌堆，可以正常打出。
- **一次性**：那场战斗结束后，无论是否打出，它都会从牌堆移除，不会进入持久牌组。
- **SL 一致性**：掉落结果在存档点预掷并写入存档，读档、保存退出、快速 SL 都不会重掷。

## 安装

**创意工坊（推荐）**：在 Steam 创意工坊订阅并在游戏内启用即可。

**手动**：把 `TarotSatchel.dll` 与 `TarotSatchel.json` 放进：

```
<游戏安装目录>/mods/TarotSatchelFull/
```

从 Steam 启动游戏，日志出现 `RUNNING MODDED` 即加载成功。本 mod 修改游戏性（`affects_gameplay: true`）：使用 modded 存档目录；多人联机时所有玩家都需要安装。

## 兼容性

已实测共存：**QuickSL**（13 次快速 SL 状态一致）、**BaseLib**、**MintySpire2**、**Intent Graph**、**LieRenTVmod**（模组角色）。

不依赖任何前置 mod；与本 mod 同 id 的副本不要同时放在 `mods/` 和工坊订阅目录，否则会重复加载。

## 已知限制

- 多人联机：塔罗袋内容按玩家本地保存，未做同步。
- 事件选项触发的战斗（如 Punch Off）暂时不会掉落塔罗牌。
- 栏位满时拒绝领取（暂无「选择替换」弹窗）。

## 开发

- 纯 C# 类库，`net10.0`，引用游戏自带的 `0Harmony.dll`、`GodotSharp.dll`、`sts2.dll`（构建不需要 Godot 编辑器）。
- 入口：`[ModInitializer]` + `ModHelper.SubscribeForRunStateHooks`；run 模型 `SatchelRunModel : RunModel`。
- 奖励：`TarotCardReward : SpecialCardReward`；掉落预掷与检查点写盘见 `官方化改造清单.md`。
- 注入：战斗开始/进房时洗牌前同步加入抽牌堆（随机位置），`BeforeHandDraw` 用官方列表预览同时播放。
- 存档：只在官方检查点推进时写 mod 状态（`RunManager.ToSave` 的 `PreFinishedRoom` 身份、地图点、房间入口）。
- 状态文件：`%APPDATA%/SlayTheSpire2/TarotSatchel/<种子>.json`。
- 文档：`AGENTS.md`（规则与官方蓝图）、`官方化改造清单.md`（实现、SL 语义、兼容性记录）、`待办.md`。

### 构建

```powershell
cd TarotSatchel; dotnet build -c Release
# 产物：TarotSatchel/bin/Release/TarotSatchel.dll
```

### 部署到游戏（本地测试）

```powershell
powershell -File scripts/deploy.ps1
```

### 发布到创意工坊

```powershell
powershell -File scripts/prepare-workshop.ps1       # 生成 workshop/upload/TarotSatchel/
cd workshop/upload
.\ModUploader-win-x64\ModUploader.exe upload -w TarotSatchel
```

物品 ID 记录在 `workshop/mod_id.txt`（`scripts/prepare-workshop.ps1` 会自动带入工作区）。

### 调试

游戏日志：`%APPDATA%/SlayTheSpire2/logs/godot.log`，搜索 `[TarotSatchel]`、`[reward]`、`[combat]`、`[use]`、`[store]`。

常用环境变量：

- `TAROT_SATCHEL_TEST=1`：启用测试驱动（自动开局/测试流程）
- `TAROT_SATCHEL_PASSIVE=1`：保留驱动但把操作交还玩家
- `TAROT_SATCHEL_RELICS=PHIAL_HOLSTER,...`：开局添加指定遗物
- `TAROT_SATCHEL_SLOTS=<1..3>`：临时覆盖栏位数量（上限 3）

## 素材说明

`workshop/icon.png`（工坊封面）使用《杀戮尖塔 2》官方塔罗牌「塔」的卡图，版权归 MegaCrit Games 所有，仅用于标识本 mod，**不在** PolyForm Noncommercial 协议的授权范围内；游戏内其他卡面/图像同理。

