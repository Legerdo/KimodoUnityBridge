# 开发文档导航

本目录面向包维护者和 AI Agent，不是普通使用者的入门文档。用户请先阅读根目录的 [`README.md`](../README.md) 或 [`README.zh-CN.md`](../README.zh-CN.md)。

## 文档职责

- [`AGENTS.md`](AGENTS.md)：仓库维护、协作和验证规则。
- [`DEVELOPMENT.md`](DEVELOPMENT.md)：当前命令面和产品边界的开发快照。
- [`plan.md`](plan.md)：文档与兼容性维护计划。
- [`animation-workflow-test-cases.md`](animation-workflow-test-cases.md)：四阶段交互式验收测试案例。
- [`KIMODO_CPP_INTEGRATION_TODO.md`](KIMODO_CPP_INTEGRATION_TODO.md)：原生后端接入 TODO。
- 根目录 [`SKILL.md`](../SKILL.md)：稳定入口、环境准备和四阶段导航。
- [`tools/common.md`](../tools/common.md)：公共命令、状态、history 和证据规则。
- 四篇 [`tools/phase-*.md`](../tools/)：语义识别、约束构造、动画生成、分析评价。
- [`Command/help.json`](../Command/help.json)：实时命令与参数 schema。
- [`archive/`](../archive/)：已被新阶段文档替代的旧能力手册，仅供追溯。

## 交接说明

执行任务时从根目录 `SKILL.md` 开始，按任务需要读取阶段文档；命令调用时再查 `Command/help.json` 或实时 `kimodo_help`。归档材料不作为当前工作流指令。

修改命令或执行规则时，更新其归属文档，并运行静态链接/命令审计与 `git diff --check`。
