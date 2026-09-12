# MES X-Ray 五分钟讲稿（1024 Demo）

对应设计文档 §14 的七个阶段和 [`../demo-script.md`](../demo-script.md) 的操作细节。全程离线运行 `fixtures/pick-order-details`，不连生产、不执行 SQL、不改任何参数或数据。

- 演示订单 **PICK0843858**，物料 **T12288**（Bracket, left, zinc plated），运行时 trace `trace-demo-001`
- 开场前执行 `XRAY_LANG=zh scripts/xray.sh demo`：编译、启动、打印所有场景的深链、在浏览器打开场景 1。5173 被占用时脚本会自动换端口并打印实际 URL，下面的链接以打印结果为准
- 每个场景都有深链。点错了就贴链接，不要现场找回路
- 界面右上角 EN / 中文 可随时切换；AI 解释会用新语言重新生成，证据 id、取值和结论不变

时间预算（总计 5:00）

| 阶段 | 时间 | 场景 | 深链 |
|---|---|---|---|
| 1 | 0:00 - 0:30 | 架构全图 | `/?lang=zh` |
| 2 | 0:30 - 1:15 | 溯源 availableQuantity | `/?field=availableQuantity&lang=zh` |
| 3 | 1:15 - 2:00 | 实时追踪 | `/?order=PICK0843858&field=availableQuantity&scope=T12288&lang=zh` |
| 4 | 2:00 - 3:15 | 调查：UDF 未知 | `/?order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1&lang=zh` |
| 5 | 3:15 - 3:55 | WMS_Enabled 的影响 | `/?impact=param:WMS_Enabled&lang=zh` |
| 6 | 3:55 - 4:35 | storageBin 血缘 | `/?order=PICK0843858&field=json:pickOrderRows.pickStorageBin.storageBin&scope=T12288&lang=zh` |
| 7 | 4:35 - 5:00 | 回到全图 | `/?lang=zh` |

---

## 阶段 1（0:00 - 0:30）页面上只是一个 0

![架构全图](01-architecture.png)

**动作**：打开场景 1，默认是「架构」视图。停一秒，让全图自己说话。

**台词**：
> 拣货单详情页上，T12288 这一行的 Available Quantity 显示 0。就一个 0。
> 这张图是这个 0 背后的全部链路：Web VP 页面、EBBA API、Controller、Service、Query，四个存储过程，下面是表、UDF 和系统参数。一共 183 个节点、307 条边，都是扫描器从代码和 SQL 里读出来的，不是手画的。
> 今天要回答的问题只有一个：这个 0 是怎么来的，我们能不能证明它。

**看点**：右下角状态栏「183 节点、307 边、86 血缘、4 未知、2 待补充、3 已知缺口」。未知和缺口是公开的，不藏。

## 阶段 2（0:30 - 1:15）溯源：Web 到 SQL

![溯源 availableQuantity](02-trace-source.png)

**动作**：在左侧响应树点 **availableQuantity**（高亮色的是演示重点字段）。图切到「实时追踪」视图并聚焦这条链，右侧检视器打开「溯源」页。

**台词**：
> 一次点击。上面的执行路径是静态调用链：页面、`GET /cwp/v1/picking/pickOrder`、`GetPickOrder`、`GetPickOrderDetails`、`GetPickOrderRows`、Query 层的 `GetPickOrderRows`、存储过程 `AP_Pick_GetPickOrderRows`，最后到函数 `AF_Pick_GetAvailableQuantity`。
> 下面是上游血缘：JSON 字段序列化自属性，Dapper 按名映射自结果列，结果列是中间列 `TotalAvailableQuantity` 的别名，它是 `SUM(MR.AvailableQuantity)`，再往上是一个 CASE 表达式。
> 这个 CASE 按 `@WMS_Enabled` 分两支：0 走本地库存乘 10，1 走 UDF。两条分支的条件都在图上，参数是一条「受控于」的边。

**看点**：图沿数据流方向排布，自下而上；边上的标签就是关系类型。图上最下面那个虚线框是 UDF，已经标了「未知：需要更多证据」。

## 阶段 3（1:15 - 2:00）实时追踪：把真实取值叠上去

![实时追踪](03-live-trace.png)

**动作**：顶部输入 PICK0843858，点 **追踪**，物料选 T12288（或直接贴场景 3 的深链）。

**台词**：
> 现在把一次真实调用（脱敏后的 fixture）叠到图上。响应树变成了真实响应，图上的节点带上了取值。
> T12288：pickQuantity = 18，onHandQuantity = 0，allocatedQuantity = 18，availableQuantity = 0。`WMS_Enabled` 的运行时取值是 1。
> 注意来源：pickQuantity 是 `AP_Pick_GetPickOrderRows` 里对 `APPQD.Quantity` 的求和，onHand 和 allocated 来自 `AP_Pick_GetPickStorageBin`，三个数最后都落在表列上。Available 不一样：它是同一个存储过程里的 CASE，`WMS_Enabled = 1` 时走 UDF。所以它是 0 而别的不是，这不是巧合，是来源不同。

**看点**：节点上的取值芯片带 `T12288:` 前缀，表示是行级作用域；顶部 FIXTURE 徽标和 trace id 一直可见，说明数据来自哪里。

## 阶段 4（2:00 - 3:15）调查：缺定义就说缺定义

![调查](04-investigate.png)

**动作**：点右上角 **调查**。

**台词**：
> 让调查器解释这个 0。结论是「需要更多证据」，置信度 75%，12 条事实，32 项证据，2 个未知。
> 先看可审计步骤：定位焦点节点、叠加 trace、重建执行路径、遍历 11 跳、按 `WMS_Enabled = 1` 评估 CASE 条件、检查定义。这是它做了什么，不是它怎么想的。
> 每条事实后面都挂着证据芯片：`ev-067` 是运行时证据，其余是节点 id 和边 id。点任何一个芯片，图和检视器都会跳到对应节点。
> 关键的一条：本次 trace 生效的分支是 `@WMS_Enabled = 1`，走 `AF_Pick_GetAvailableQuantity`。
> 然后是两条假设，状态都是「未验证」：一，AvailableQuantity 在这个 UDF 内部决定，而它的定义我们没有，所以观测值无法继续验证；二，观测到的 0 可能只是 `ISNULL(..., 0)` 的回退值，也就是函数根本没返回行。每条假设都附了一个只读的核查步骤。
> 这就是这个项目对 AI 的定位：没有证据就不下结论，说不知道，然后告诉你下一步去拿什么证据。

**看点**：审计块里有 provider、model、prompt 版本、时间戳和引用的证据 id 数。切一下 EN / 中文：句子换语言，证据 id、取值和结论完全不变。

问答备用：
- 「这是 LLM 吗？」演示默认是离线规则引擎（provider `rules`）。接 OpenAI 兼容模型时，输出走同一个证据绑定校验器：引用了不存在证据的句子会被降级为假设，没有任何证据支撑的回答会被判为 Unknown。
- 「它会不会改数据？」不会。运行时适配器是只读白名单工具，第一版没有 UPDATE、DELETE、任意 SQL 和自动修复。状态栏那句话就是策略。

## 阶段 5（3:15 - 3:55）影响：一个参数怎么走到页面上

![影响分析](05-impact.png)

**动作**：点图上的 `WMS_Enabled` 节点（或阶段 4 里的证据芯片），再点 **影响**。

**台词**：
> 反过来问：如果有人切 `WMS_Enabled`，会影响什么？19 个下游节点。
> 关键路径两条：一条沿调用链，`WMS_Enabled` 到 `AP_Pick_GetPickOrderRows`、Query、Service、Controller、API，最后到 Web VP 页面；另一条沿数据流，CASE 表达式、中间列、结果列、`CWPPickOrderRow.AvailableQuantity`，最后到 JSON 字段 `availableQuantity`。
> 这是切参数之前该看的图，而不是切完以后现场排查。

**看点**：受影响节点按距离分组；每条路径都可以点进去。

## 阶段 6（3:55 - 4:35）storageBin：FIFO 和 STRING_AGG

![storageBin 血缘](06-storage-bin.png)

**动作**：响应树点 `pickStorageBin.storageBin`（或贴场景 6 的深链）。

**台词**：
> 换一个复杂一点的字段。库位是 `STRING_AGG` 把 `#LocationList.StorageBin` 按 FIFO 顺序拼起来；FIFO 排名是 `ROW_NUMBER() OVER (ORDER BY FirstInventoryOn)`，`FirstInventoryOn` 是 `MIN(CreatedOn)`；库位本身又是一个 CASE，看 `WL_Description_Replacement`，ELSE 走 `WAREHOUSE_LOCATION.Location`。
> 实时取值 `"A-01-02,A-01-05"` 一路挂在链上。这条链每一跳都是已知的，溯源页上的徽标是一个对勾。

**对照（可选，20 秒）**：`destinationWagon.storageBin.location`。

![目的车库位](06b-destination-wagon.png)

> 这条链停在 `GetStorageBin`，它标的是「待补充」：这个存储过程按站点配置、运行时决定，第一版故意没扫。X-Ray 明确地停在这里，而不是猜一个。

## 阶段 7（4:35 - 5:00）回到全图

**动作**：顶部点 **架构**。

**台词**：
> 回到全图收尾。X-Ray 不是一个聊天框。扫描器把代码和 SQL 变成证据图，运行时 trace 把真实取值绑到图上，AI 只允许基于这些证据说话，说不出来就说 Unknown。
> 状态栏一直写着我们的边界：只读、白名单工具、不执行 SQL、不改参数和数据。
> 下一步很具体：拿到 `AF_Pick_GetAvailableQuantity` 的脱敏定义并重新扫描，今天这个「需要更多证据」就会变成「已知」，这是我们要给业务看的第一个闭环。

---

## 彩排检查单

1. `scripts/xray.sh status`：api 与 web 都是 healthy；记下 web 的实际端口。
2. 打开场景 4 的深链：应看到「需要更多证据」、75%、12 条事实。这一条通了，说明 API、图、runtime fixture、调查器全部在线。
3. 浏览器缩放 100%，窗口不小于 1280 宽；1400 以下会隐藏副标题和图例，这是设计行为。
4. 语言：演示前决定用 EN 还是中文，并在浏览器里切一次，让它记住。
5. 关掉其它占端口的项目不是必须的，脚本会自己换端口；但不要在演示中途重启它们。
6. 出错：贴当前场景的深链；API 不通时 `scripts/xray.sh restart --api-only --no-build`。

## 截图

本目录下的 PNG 由无头 Chrome 在 1600 x 1000 下生成，UI 语言为中文；`04-investigate-en.png` 是同一场景的英文版。它们只是讲稿的配图，现场以运行中的界面为准。
