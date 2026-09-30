# 落地计划目录说明

本目录只放落地计划与进度记录。

## 已删除的历史内容

审计归档 `audit-*` 目录与审计过程文档（`文档代码一致性审计_*.md`、`文档代码深度审核_*.md`）已于本次提交
删除：它们是外部审计的证据归档（复现工程、日志、报告），属于生成物，不进 git，git 历史即备份。
各处文档、脚本注释、ADR 里对 `audit-<sha>-<日期>/…` 路径的引用是历史引文，不逐条订正；需要原文时按
`git show <sha>:<路径>` 取回，其中 `<sha>` 填删除前的提交 `eb5a5e26`，例如：

```
git show eb5a5e26:architecture/落地计划/audit-c9ff301-20260909/docs-project/api-compat/FieldSchemaConsumer.csproj
```

（目录说明待文档归位完成后补全。）
