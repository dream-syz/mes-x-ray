**MES X-Ray**

**Web Visual Picking - Pick Order Details  
POC 详细设计文档**

目标：把页面字段追踪到 EBBA API、.NET 方法、Stored Procedure、SQL
表/函数与运行时证据，形成可解释的系统 X-Ray。

| **项目属性** | **内容**                                                                            |
|--------------|-------------------------------------------------------------------------------------|
| 项目类型     | 内部技术 POC / 1024 Programmer's Day Demo                                           |
| 首个 Case    | Web Visual Picking - Get Pick Order Details                                         |
| 推荐技术栈   | .NET 10 / ASP.NET Core, React, SQL Server, Roslyn, SQL Parser/规则引擎, LLM/Agent   |
| 设计原则     | Evidence First；No evidence, no conclusion；只读优先；AI 不直接获得生产数据库写权限 |
| 文档版本     | v1.0                                                                                |
| 日期         | 2026-09-10                                                                          |

**安全说明：本文档已使用占位符替代真实主机、密码、JWT
等凭据。禁止把生产凭据写入源码、提示词、日志或演示数据。**

# 1. 项目概述

MES X-Ray
不是一个聊天机器人，而是一个面向企业软件开发者的“系统可解释性与数据血缘平台”。第一版以
Web Visual Picking 的 Pick Order Details 为代表性 Case，自动建立从
UI/API 到 .NET、Stored Procedure、SQL 对象和字段的依赖关系，并在给定
Pick Order 时叠加运行时证据。

用户最终看到的是可点击、可追踪、可验证的 Evidence
Graph：某个页面字段为什么是这个值、由哪个 SP
产生、受哪个系统参数控制、读取了哪些表，以及修改某个对象可能影响哪些上游页面/API。AI
负责解释、归纳、调查与生成业务语义；确定性的依赖关系优先由静态扫描和元数据构建。

## 1.1 核心价值

| **问题**      | **传统方式**                 | **MES X-Ray**                          |
|---------------|------------------------------|----------------------------------------|
| 页面字段来源  | 人工查前端、API、Service、SP | 点击字段直接 Trace Source              |
| 修改影响      | 全局搜索 + 经验判断          | 反向依赖图 + Impact Analysis           |
| 复杂 SQL 理解 | 逐段阅读 SP/UDF              | 自动提取字段表达式、表、函数、系统参数 |
| 故障调查      | 人手动切换多个系统           | 结构图 + Runtime Evidence 联合调查     |
| 新人接手      | 依赖资深同事口述             | 系统自动生成业务执行链和证据           |

## 1.2 POC 成功定义

- 从 availableQuantity 追踪到
  CWPPickOrderRow.AvailableQuantity、GetPickOrderRows、AP_Pick_GetPickOrderRows
  及 SQL 计算表达式。

- 识别 WMS_Enabled 对 AvailableQuantity 计算路径的控制关系。

- 追踪 PickStorageBin.StorageBin / OnHandQuantity / AllocatedQuantity 到
  AP_Pick_GetPickStorageBin 的具体 SQL 来源。

- 展示 DestinationWagon 聚合关系，并把尚未扫描到的 GetStorageBin 标记为
  Unknown，而不是让 AI 猜测。

- 支持 Trace Source、Impact Analysis、Architecture 和 Runtime Trace。

- AI 输出必须绑定 Evidence Node；无证据时返回 Unknown / Need More
  Evidence。

## 1.3 POC 非目标

- 不解析整个 Apriso 平台；本 Case 本身不经过 Apriso Operation。

- 不在第一版支持 Multi Pick Order 全链路。

- 不允许 AI 自动修改生产 SQL、System Parameter 或业务数据。

- 不追求一次性覆盖所有 EBBA API。

- 不把 LLM 当作 C#/SQL parser 的替代品。

# 2. 当前业务链路与已知事实

Authentication Context  
Web VP -\> Auth API -\> JWT/User Context -\> Web VP Session  
\|  
+-\> facility / roles / workCenters ...  
  
Business Flow  
Web VP  
-\> GET /cwp/v1/picking/pickOrder  
-\> EBBA API GetPickOrderDetails()  
-\> StackWagonModel  
-\> LanguageId  
-\> AP_Wrapper_Pick_GetPickOrderDetail \[Header\]  
-\> foreach PickOrder  
-\> IsMultiPickOrder?  
-\> false: GetPickOrderRows()  
-\> AP_Pick_GetPickOrderRows \[Base Row\]  
-\> foreach Material  
-\> AP_Pick_GetPickStorageBin \[Enrichment\]  
-\> GetDestinationWagon()  
-\> AP_Pick_GetDestinationWagon  
-\> foreach Wagon  
-\> GetStorageBin() \[Pending\]

## 2.1 Response 聚合树

CWPPickOrderModel  
├─ Header fields  
└─ PickOrderRows\[\]  
├─ Base material fields -\> AP_Pick_GetPickOrderRows  
├─ PickStorageBin -\> AP_Pick_GetPickStorageBin  
└─ DestinationWagon\[\]  
├─ PhysicalWagonId / NumberOfBoxes -\> AP_Pick_GetDestinationWagon  
└─ StorageBin\[\] -\> GetStorageBin() \[Pending\]

## 2.2 已确认字段级血缘

| **Response 字段**                | **来源**                    | **关键逻辑**                                                                             |
|----------------------------------|-----------------------------|------------------------------------------------------------------------------------------|
| availableQuantity                | AP_Pick_GetPickOrderRows    | WMS_Enabled=0: SUM(Quantity)\*10；=1: AF_Pick_GetAvailableQuantity；随后按 Material 汇总 |
| pickQuantity                     | AP_Pick_GetPickOrderRows    | SUM(AT_PICK_PRINT_QUEUE_DETAIL.Quantity)                                                 |
| materialNumber                   | AP_Pick_GetPickOrderRows    | PRODUCT.ProductNo                                                                        |
| materialDescription              | AP_Pick_GetPickOrderRows    | AF_GetTextTranslation(PRODUCT.TextID, LanguageId, 'Medium')                              |
| materialImageUrl                 | AP_Pick_GetPickOrderRows    | AF_Pick_GetProductImageURL(...)                                                          |
| pickStorageBin.storageBin        | AP_Pick_GetPickStorageBin   | Warehouse Location 去重后按最早库存时间 FIFO 排序并 STRING_AGG                           |
| pickStorageBin.onHandQuantity    | AP_Pick_GetPickStorageBin   | SUM(APPQD.PickedQuantity)                                                                |
| pickStorageBin.allocatedQuantity | AP_Pick_GetPickStorageBin   | SUM(APPQD.Quantity - PickedQuantity)                                                     |
| destinationWagon.numberOfBoxes   | AP_Pick_GetDestinationWagon | AT_PICK_GROUP.NoOfBins                                                                   |
| destinationWagon.physicalWagonId | AP_Pick_GetDestinationWagon | 当前固定返回空字符串                                                                     |

关键约束：AvailableQuantity、OnHandQuantity、AllocatedQuantity
是不同来源/语义，不能仅因数值相关就推断因果。

# 3. 目标架构

┌───────────────────────────────┐  
│ MES X-Ray UI │  
│ Architecture \| Live Trace │  
│ Trace Source \| Impact \| Explain│  
└───────────────┬───────────────┘  
│  
ASP.NET Core X-Ray API  
│  
┌──────────────────┼──────────────────┐  
│ │ │  
Graph Service Evidence Service AI Investigator  
│ │ │  
└──────────────┬───┴───────┬──────────┘  
│ │  
Metadata Store Tool Gateway  
Node / Edge (read-only)  
│ │  
┌─────────────────┼───────────┼─────────────────┐  
│ │ │ │  
Web VP Scanner .NET Scanner SQL Scanner Runtime Adapter  
TS/HTTP metadata Roslyn SP/UDF/Table Test DB/API/Logs

## 3.1 设计原则

- Deterministic first：Route、方法调用、SP 名、表/函数优先由
  parser/metadata 确定。

- AI second：LLM 用于业务语义、复杂表达式解释、调查计划与证据归纳。

- Evidence-bound：每个事实结论关联 Evidence IDs。

- Read-only by default：POC 不执行任意 SQL。

- Progressive disclosure：先业务级，再展开技术细节。

- Static + Runtime separation：Architecture 描述可能路径，Runtime
  Evidence 描述本次实际值。

# 4. 子系统设计

| **组件**        | **职责**                                     | **建议技能**          |
|-----------------|----------------------------------------------|-----------------------|
| X-Ray Web UI    | 系统地图、字段树、节点详情、Live Trace       | React/TypeScript      |
| X-Ray API       | Graph 查询、Trace/Impact、AI orchestration   | ASP.NET Core/C#       |
| .NET Scanner    | Controller/Service/Query/Dapper/SP 调用      | C#/Roslyn             |
| SQL Scanner     | SP/UDF、SELECT alias、表、函数、参数、表达式 | T-SQL/C#              |
| Metadata Store  | Node/Edge/FieldLineage/Evidence              | SQL Server/SQLite     |
| Runtime Adapter | 脱敏测试数据和只读诊断                       | C#/SQL                |
| AI Investigator | Explain/Investigate/Impact                   | LLM/Structured Output |
| Demo Fixture    | 固定 Case、演示数据、测试                    | Backend/QA            |

## 4.1 .NET Scanner

| **识别对象**        | **示例**                                                | **生成关系**                           |
|---------------------|---------------------------------------------------------|----------------------------------------|
| HTTP Endpoint       | \[HttpGet\] + Route                                     | API_ENDPOINT -\> HANDLED_BY -\> METHOD |
| Method Call         | GetPickOrderRows(...)                                   | METHOD -\> CALLS -\> METHOD            |
| Dapper SP           | QueryAsync\<T\>("\[dbo\].\[AP_Pick_GetPickOrderRows\]") | METHOD -\> EXECUTES_SP -\> SP          |
| DTO/Model           | CWPPickOrderRow                                         | METHOD -\> RETURNS -\> MODEL           |
| Property Assignment | row.PickStorageBin = ...                                | FIELD -\> ENRICHED_BY -\> METHOD       |
| Branch              | if (pickOrder.IsMultiPickOrder)                         | METHOD -\> BRANCHES_ON -\> FIELD       |

输出需保存 source path、line range、symbol full
name、confidence、scanner version。

## 4.2 SQL Scanner

| **Pattern**      | **示例**                          | **关系**                              |
|------------------|-----------------------------------|---------------------------------------|
| System Parameter | ...('WMS_Enabled')                | SP -\> USES_PARAMETER                 |
| Table Read       | FROM AT_PICK_PRINT_QUEUE_DETAIL   | SP -\> READS -\> TABLE                |
| UDF Call         | AF_Pick_GetAvailableQuantity(...) | FIELD -\> COMPUTED_BY -\> FUNCTION    |
| Alias            | ... AS AvailableQuantity          | EXPR -\> PRODUCES -\> FIELD           |
| CASE             | CASE @WMS_Enabled                 | FIELD -\> CONTROLLED_BY -\> PARAMETER |
| Aggregate        | SUM(APPQD.Quantity)               | FIELD -\> DERIVED_FROM -\> COLUMN     |
| Temp/CTE         | MainResults / MaterialTotals      | 可作为中间节点或折叠展示              |

推荐 ScriptDom + 规则层。LLM 只用于补充解释，不作为唯一 SQL 解析器。

## 4.3 Runtime Adapter

Allowed:  
trace_pick_order(orderNo, facility, pickGroup, user)  
trace_material(orderNo, materialNo)  
read_system_parameter(name)  
read_pick_order_response(fixtureId)  
  
Forbidden:  
execute_arbitrary_sql(sql)  
update_system_parameter(...)  
update_pick_order(...)

# 5. Metadata / Evidence 数据模型

## 5.1 Node

| **字段**             | **说明**                                                                    |
|----------------------|-----------------------------------------------------------------------------|
| Id                   | 稳定 ID，例如 sp:dbo.AP_Pick_GetPickOrderRows                               |
| Type                 | Page/API/Method/Model/Field/SP/Function/Table/Column/SystemParameter/Branch |
| Name / QualifiedName | 显示名与全限定名                                                            |
| Layer                | Web/API/Service/Data/Config                                                 |
| SourcePath + Range   | 源码/SQL 定位                                                               |
| Metadata             | Route、params、表达式等                                                     |
| ScanVersion          | 扫描器版本                                                                  |

## 5.2 Edge

| **字段**              | **说明**                                                                                                 |
|-----------------------|----------------------------------------------------------------------------------------------------------|
| FromNodeId / ToNodeId | 关系两端                                                                                                 |
| RelationType          | CALLS/EXECUTES_SP/READS/RETURNS/MAPS_TO/SERIALIZES_AS/CONTROLLED_BY/DERIVED_FROM/ENRICHED_BY/BRANCHES_ON |
| Confidence            | 1.0 静态确定；较低值表示推导                                                                             |
| EvidenceType          | Roslyn/SQLParser/Runtime/Manual                                                                          |
| EvidenceRef           | 文件行号/SQL object/trace id                                                                             |

## 5.3 FieldLineage

| **字段**        | **说明**                                           |
|-----------------|----------------------------------------------------|
| OutputFieldId   | 例如 JSON availableQuantity                        |
| SourceFieldId   | 例如 TotalAvailableQuantity                        |
| TransformType   | Direct/Aggregate/Conditional/Mapping/Serialization |
| Expression      | 如 SUM(AvailableQuantity)                          |
| Condition       | 如 WMS_Enabled=1                                   |
| EvidenceEdgeIds | 支撑边集合                                         |

## 5.4 Runtime Evidence

| **字段**               | **说明**                                       |
|------------------------|------------------------------------------------|
| TraceId                | 一次 Live Trace                                |
| EntityType/Key         | PickOrder/PICK0843858                          |
| NodeId                 | 对应架构节点                                   |
| EvidenceType           | API_RESPONSE/QUERY_RESULT/PARAMETER/LOG/TIMING |
| Value                  | 脱敏 JSON/value                                |
| ObservedAt/Environment | 时间与环境                                     |

# 6. 首个 Field-Level Lineage：AvailableQuantity

Web VP availableQuantity  
↑ SERIALIZES_AS  
CWPPickOrderRow.AvailableQuantity  
↑ DAPPER_MAPS  
AP_Pick_GetPickOrderRows result: AvailableQuantity  
↑ ALIAS_OF  
MaterialTotals.TotalAvailableQuantity  
↑ DERIVED_FROM  
SUM(MainResults.AvailableQuantity)  
↑ CONDITIONAL  
CASE @WMS_Enabled  
├─ 0 -\> ISNULL(SUM(APPQD.Quantity) \* 10, 1000)  
└─ 1 -\> ISNULL(AF_Pick_GetAvailableQuantity(  
@Facility, APP.WarehouseLocationID, APP.ProductID), 0)

WMS_Enabled=1 时，AF_Pick_GetAvailableQuantity
是下一跳。若函数定义未扫描，UI 必须显示 Need More Evidence。

## 6.1 PickStorageBin

StorageBin  
\<- STRING_AGG(#LocationList.StorageBin, ',')  
\<- \#LocationDistinct  
\<- WAREHOUSE_LOCATION.Location  
OR translated WL.TextID when WL_Description_Replacement=true  
\<- FIFO order by MIN(INVENTORY2.CreatedOn)  
  
OnHandQuantity  
\<- SUM(AT_PICK_PRINT_QUEUE_DETAIL.PickedQuantity)  
  
AllocatedQuantity  
\<- SUM(AT_PICK_PRINT_QUEUE_DETAIL.Quantity - ISNULL(PickedQuantity, 0))

## 6.2 System Parameter Impact

| **Parameter**                    | **直接影响**               | **Impact 路径**                           |
|----------------------------------|----------------------------|-------------------------------------------|
| WMS_Enabled                      | AvailableQuantity 计算分支 | Parameter -\> SP -\> Field -\> API -\> UI |
| PickOrder_Type                   | Rows/StorageBin 查询过滤   | Parameter -\> SP filter -\> rows          |
| PickDocumentClassForProductImage | MaterialImageUrl           | Parameter -\> UDF -\> field               |
| ChildContainerCalssID            | FIFO inventory filtering   | Parameter -\> SP filter -\> StorageBin    |
| WL_Description_Replacement       | StorageBin 表示            | Parameter -\> CASE -\> StorageBin         |

# 7. X-Ray API 设计

| **Method** | **Route**                                     | **用途**           |
|------------|-----------------------------------------------|--------------------|
| GET        | /api/xray/cases/pick-order-details            | Case 概览          |
| GET        | /api/xray/graph?root={nodeId}&depth=3         | 架构子图           |
| GET        | /api/xray/nodes/{id}                          | 节点详情           |
| GET        | /api/xray/trace/field?field=availableQuantity | 字段血缘           |
| GET        | /api/xray/impact/{nodeId}                     | 反向影响           |
| POST       | /api/xray/runtime/pick-order                  | Live Trace         |
| POST       | /api/xray/ai/explain                          | 基于 Evidence 解释 |
| POST       | /api/xray/ai/investigate                      | 只读调查           |

## 7.1 AI Explain 契约

Request:  
{  
"question": "Why is Available Quantity 0?",  
"traceId": "trace-demo-001",  
"focusNodeId": "field:CWPPickOrderRow.AvailableQuantity",  
"allowedEvidenceIds": \["ev-001","ev-014","ev-003"\]  
}  
  
Response:  
{  
"summary": "...",  
"knownFacts": \[{"text":"...", "evidenceIds":\["ev-001"\]}\],  
"hypotheses": \[{"text":"...", "status":"unverified"}\],  
"unknowns": \["AF_Pick_GetAvailableQuantity definition not scanned"\],  
"confidence": 0.86  
}

# 8. 前端 UX 设计

┌──────────────────────────────────────────────────────────────────────┐  
│ MES X-Ray \| Architecture \| Live Trace \| Pick Order: PICK0843858 │  
├───────────────┬──────────────────────────────────┬───────────────────┤  
│ Response Tree │ Evidence Graph │ Inspector │  
│ Header │ Web VP │ availableQuantity │  
│ Rows │ ↓ │ Value: 0 │  
│ T12288 │ EBBA API │ Source: SP │  
│ available=0 │ ↓ │ \[Trace Source\] │  
│ storageBin │ GetPickOrderDetails │ \[Impact\] │  
│ allocated=18│ ↓ │ \[Explain\] │  
│ │ AP_Pick_GetPickOrderRows │ Evidence: 5 │  
│ │ ↓ │ │  
│ │ WMS_Enabled -\> CASE -\> UDF │ │  
└───────────────┴──────────────────────────────────┴───────────────────┘

- 点击 Response 字段自动聚焦 FieldLineage。

- Trace Source 向下展开到 SQL/UDF/Table；未知节点明确 Pending/Unknown。

- Impact Analysis 反向展开到 API/Web 字段。

- Explain 显示 Evidence 数量、Known Facts 和 Unknowns。

- Architecture 显示静态路径；Live Trace 叠加实际值。

# 9. AI Investigator

AI 是证据解释器和调查编排器，不是数据库管理员。LLM
不保存真实系统事实，事实来自
Graph/Evidence。禁止展示隐藏思维链；只展示可审计步骤、证据、假设状态和结论。

| **能力**         | **输入**                     | **输出**               |
|------------------|------------------------------|------------------------|
| Explain Node     | 节点 + Evidence              | 业务含义、上下游       |
| Explain Field    | FieldLineage + Runtime value | 来源、计算、条件       |
| Investigate      | 问题 + TraceId               | 调查步骤、事实、未知项 |
| Impact Summary   | 反向子图                     | 受影响 API/UI/字段     |
| Code-to-Business | AST/SQL摘要                  | 业务流程描述           |

# 10. 安全与治理

- 真实主机、密码、JWT 不进入文档/Git；全部使用假数据/占位符。

- 连接串使用 Secret Manager/环境变量。

- Runtime Adapter 使用只读账号和白名单操作。

- 查询限制超时、最大行数和允许环境。

- 日志脱敏 username/token/host/个人信息。

- AI Provider 必须符合公司数据分类策略。

- AI 结论记录 evidenceIds、model/version、timestamp。

- 第一版不实现 UPDATE/DELETE/任意 SQL/自动修复。

# 11. 推荐 Solution 结构

mes-xray/  
├─ src/  
│ ├─ MesXray.Api/  
│ ├─ MesXray.Domain/  
│ ├─ MesXray.Graph/  
│ ├─ MesXray.Scanner.DotNet/  
│ ├─ MesXray.Scanner.Sql/  
│ ├─ MesXray.Runtime/  
│ ├─ MesXray.AI/  
│ └─ MesXray.Web/  
├─ tests/  
│ ├─ Scanner.DotNet.Tests/  
│ ├─ Scanner.Sql.Tests/  
│ ├─ Graph.Tests/  
│ └─ Integration.Tests/  
├─ fixtures/pick-order-details/  
└─ docs/

| **领域**     | **建议**                         | **理由**               |
|--------------|----------------------------------|------------------------|
| Backend      | .NET 10 / ASP.NET Core           | 当前 LTS；见 ADR-0002  |
| Frontend     | React + TypeScript               | 交互式 Graph/Inspector |
| Graph UI     | React Flow 或 Cytoscape.js       | POC 快                 |
| Metadata DB  | SQLite（Demo）或 SQL Server      | 暂不需要 Neo4j         |
| C# Analysis  | Roslyn                           | 语义级分析             |
| SQL Analysis | ScriptDom + 规则层               | T-SQL 结构化解析       |
| AI           | 公司批准 LLM + structured output | 可替换                 |
| Realtime     | HTTP；必要时 SignalR             | 避免过度设计           |

# 12. 工作拆分与人员安排

| **角色**               | **主要任务**                         | **交付物**                 |
|------------------------|--------------------------------------|----------------------------|
| A Backend/Architecture | Domain、Graph API、Metadata、Runtime | API + schema + integration |
| B Static Analysis      | Roslyn、SQL Scanner、Field Lineage   | scanner + expected graph   |
| C Frontend             | Map、Tree、Inspector、Trace/Impact   | Web UI                     |
| D AI/Demo/QA           | Evidence prompt、fixtures、demo      | AI service + tests + demo  |

## 12.1 建议 4 周计划

| **周期** | **目标**       | **主要任务**                                                 | **退出条件**                        |
|----------|----------------|--------------------------------------------------------------|-------------------------------------|
| Week 1   | Ground Truth   | repo/schema；手工 expected graph；UI wireframe；脱敏 fixture | 静态 JSON 可展示目标图              |
| Week 2   | Automatic Scan | Roslyn + SQL scanner；导入 metadata                          | 核心关系 80% 自动生成               |
| Week 3   | Trace + AI     | Field Trace、Impact、Runtime fixture、Explain                | availableQuantity/StorageBin 端到端 |
| Week 4   | Polish         | 视觉、异常、缓存、测试、演示                                 | Demo 连续 3 次稳定                  |

## 12.2 可直接分配的 Backlog

| **ID**   | **任务**                               | **Owner** | **Priority** |
|----------|----------------------------------------|-----------|--------------|
| XRAY-001 | Node/Edge/FieldLineage/Evidence schema | A         | P0           |
| XRAY-002 | Pick Order expected graph fixture      | A+D       | P0           |
| XRAY-003 | Roslyn 方法调用扫描                    | B         | P0           |
| XRAY-004 | Roslyn Dapper SP 识别                  | B         | P0           |
| XRAY-005 | Roslyn Model/Property/branch           | B         | P1           |
| XRAY-006 | SQL 参数/表/UDF/SystemParameter        | B         | P0           |
| XRAY-007 | SQL alias/CASE/aggregate lineage       | B         | P0           |
| XRAY-008 | Graph import/query                     | A         | P0           |
| XRAY-009 | Trace Source API                       | A         | P0           |
| XRAY-010 | Impact API                             | A         | P0           |
| XRAY-011 | Runtime fixture adapter                | A+D       | P0           |
| XRAY-012 | Response Tree                          | C         | P0           |
| XRAY-013 | Evidence Graph                         | C         | P0           |
| XRAY-014 | Node Inspector                         | C         | P0           |
| XRAY-015 | Architecture/Live switch               | C         | P1           |
| XRAY-016 | AI structured output                   | D         | P0           |
| XRAY-017 | Explain AvailableQuantity              | D         | P0           |
| XRAY-018 | Unknown handling                       | A+D       | P0           |
| XRAY-019 | Security/redaction                     | D         | P0           |
| XRAY-020 | Demo + fallback recording              | D         | P0           |

# 13. 验收标准与测试

| **Case** | **Given**               | **When**                | **Then**                                      |
|----------|-------------------------|-------------------------|-----------------------------------------------|
| AC-01    | 已扫描 GetPickOrderRows | 打开方法                | 显示 EXECUTES_SP -\> AP_Pick_GetPickOrderRows |
| AC-02    | 已扫描 Rows SP          | Trace availableQuantity | 显示 WMS_Enabled CASE 两条分支                |
| AC-03    | WMS_Enabled=1 fixture   | Explain available=0     | 显示 UDF 来源；未扫描时 Unknown               |
| AC-04    | T12288 fixture          | Trace PickStorageBin    | 显示 OnHand/Allocated SQL 表达式              |
| AC-05    | 点击 WMS_Enabled        | Impact                  | 反向到 AvailableQuantity -\> API -\> Web VP   |
| AC-06    | GetStorageBin 未知      | Investigate Destination | 明确 Unknown                                  |
| AC-07    | 请求任意 SQL            | Runtime/AI              | 拒绝执行                                      |
| AC-08    | 连续三次 Demo           | 完整 Trace              | 无 crash、结果一致                            |

Scanner 单元测试使用脱敏最小 fixture，并维护 expected nodes/edges
JSON。SQL 测试覆盖 CASE、CTE、temp table、alias、UDF、STRING_AGG、OUTER
APPLY。

# 14. 1024 Demo 剧本

| **阶段** | **动作**                        | **重点**                                                      |
|----------|---------------------------------|---------------------------------------------------------------|
| 1        | 选中 T12288 availableQuantity=0 | 页面只是一个 0，背后有多少层？                                |
| 2        | Trace Source                    | Web -\> API -\> Service -\> SP -\> CASE/UDF                   |
| 3        | Live Trace                      | 显示 Pick=18、OnHand=0、Allocated=18，强调 Available 来源不同 |
| 4        | 展开 UDF                        | 缺定义时 Need More Evidence，AI 不猜                          |
| 5        | WMS_Enabled -\> Impact          | 参数如何影响页面字段                                          |
| 6        | Trace storageBin                | FIFO/STRING_AGG/Location 血缘                                 |
| 7        | 回到全图                        | AI 不是 UI，而是系统可解释性的分析引擎                        |

# 15. 风险与应对

| **风险**            | **影响**      | **应对**                                   |
|---------------------|---------------|--------------------------------------------|
| 复杂 SQL 解析不完整 | 血缘断裂      | 允许 Unknown；先覆盖 Case 语法             |
| 动态调用/Reflection | 静态无法确定  | runtime instrumentation 或 manual override |
| 前端 mapping 难     | UI 血缘不完整 | POC 手工关键字段 mapping                   |
| AI 幻觉             | 错误根因      | Evidence-bound；无 evidence 不下结论       |
| 数据泄露            | 合规          | 脱敏 fixtures + secret scanning + test env |
| Graph 太复杂        | 看不懂        | Progressive disclosure                     |
| 工期不足            | 延期          | P0 只保 availableQuantity + PickStorageBin |

# 16. 仍需补充的输入（不阻塞开工）

| **输入**                           | **用途**                    | **优先级** |
|------------------------------------|-----------------------------|------------|
| AF_Pick_GetAvailableQuantity 定义  | AvailableQuantity 追到底    | P0         |
| GetStorageBin(...) + SP            | DestinationWagon.StorageBin | P1         |
| AP_Wrapper_Pick_GetPickOrderDetail | Header 血缘                 | P1         |
| Web VP 前端 Service/Component      | API 连到真实 UI             | P1         |
| 脱敏 Pick Order fixture            | Runtime Trace               | P0         |

# 17. Definition of Done

- expected graph 有版本控制且 scanner 可自动重建大部分节点/边。

- availableQuantity 可追到 SP CASE/UDF，WMS_Enabled Impact 可反向展示。

- PickStorageBin 三个核心字段可追到 SQL 表达式。

- UI 支持 Architecture、Trace Source、Impact、Explain、Unknown。

- AI 事实结论均可定位 Evidence IDs。

- 演示数据/日志/源码均脱敏。

- README 包含启动、fixture 导入、scan、demo。

- CI 包含 scanner unit tests + graph integration tests。

- 现场 Demo 有离线 fixture，不依赖生产。

# 18. 项目负责人执行建议

第一周不要先接 LLM。先把 Ground Truth
图手工做出来，确认“系统最终应该看到什么”；第二周让 Scanner
自动重建；第三周再让 AI 消费
Graph/Evidence。这样可以避免最终退化成“一个会读代码的聊天机器人”。

如果工期压缩，最小范围只保留 availableQuantity。这个字段已足够同时展示
.NET 调用、Dapper SP、SQL CASE、System Parameter、UDF、聚合、Field
Lineage 和 Impact Analysis。

# 附录 A：本 Case Ground Truth 摘要

GetPickOrderDetails  
├─ AP_Wrapper_Pick_GetPickOrderDetail  
└─ IsMultiPickOrder  
└─ false -\> GetPickOrderRows  
├─ AP_Pick_GetPickOrderRows  
│ ├─ WMS_Enabled / PickOrder_Type / PickDocumentClassForProductImage  
│ ├─ PRODUCT / PRODUCT_ALIAS  
│ ├─ AT_PICK_PRINT_QUEUE / DETAIL / AT_PICK_PRODUCT  
│ ├─ WAREHOUSE_LOCATION / REPLENISH_STRATEGY / CONTENT  
│ ├─ AF_GetTextTranslation  
│ ├─ AF_Pick_GetAvailableQuantity  
│ └─ AF_Pick_GetProductImageURL  
├─ AP_Pick_GetPickStorageBin  
│ ├─ ChildContainerCalssID / WL_Description_Replacement  
│ ├─ INVENTORY2 / CONTAINER / AT_PICK_PRODUCT / WAREHOUSE_LOCATION  
│ └─ AT_PICK_PRINT_QUEUE / DETAIL  
└─ GetDestinationWagon  
├─ AP_Pick_GetDestinationWagon -\> AT_PICK_GROUP.NoOfBins  
└─ GetStorageBin \[Pending\]

# 附录 B：命名建议

产品名建议：MES X-Ray / System X-Ray。1024 分享标题建议：《MES X-Ray：让
AI 看懂一个字段背后的整个系统》；副标题：From UI Value to Evidence
Graph。MCP/Agent 作为实现细节，不放在主标题。
