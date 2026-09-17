# Sharkey 本地版本迭代

仓库已有本地提交历史，本轮保留旧版检查点 `62d397d`，阅读体验第一批在
`codex/reading-experience` 分支开发。没有配置或推送远程仓库。
`main` 暂时保留旧版；试用满意后再合并功能分支。

## 平时怎么用

在项目目录打开 PowerShell：

```powershell
git status
git log --oneline --decorate -10
git diff
```

每轮功能从干净工作区创建分支，例如 `git switch -c codex/next-feature`。
开发完成先构建、测试，再明确添加本轮源文件并提交：

```powershell
.\build.cmd
.\test.cmd
git add PopupWindow.cs tests/PopupInteractionProbe.cs CHANGELOG.md
git commit -m "feat: describe this iteration"
```

添加文件前先检查差异。不要提交个人配置、密钥、日志或构建输出；当前 `.gitignore`
已经排除这些常见路径。代码里的测试用假密钥只用于模拟请求。

## 旧版本运行时构建

运行中的 EXE 会被 Windows 锁定。不需要强制结束它，先输出到另一个目录：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" GlobalTranslator.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:OutputPath=bin\ReadingExperience\
.\test.cmd bin\ReadingExperience\Sharkey.exe
```

试用前从托盘退出旧版，再启动新 EXE；不要同时运行两个版本争抢快捷键。
自动测试生成的预览位于 `tmp/tests/`，只验证渲染布局，不代替混合 DPI 多屏实机验证。
构建机目前存在 MSB3644（缺少 .NET Framework 4.0 引用程序集）警告，应在规范发布环境中
补齐目标框架引用程序集并验证干净 Windows 环境；不要把本机构建通过视为所有电脑兼容。

## 对比与安全回退

```powershell
git diff main...codex/reading-experience
git show 62d397d --stat
```

要单独查看或编译旧版，可以建立独立工作目录，不覆盖当前代码：

```powershell
git worktree add ..\Sharkey-checkpoint 62d397d
```

如需撤销已经提交的功能，先保证工作区干净，再使用 `git revert <提交号>` 创建反向提交。
不要用 `git reset --hard` 清理还没有保存的修改。

## 版本与发布

版本唯一来源为 `VersionInfo.cs`，当前正式版为 `0.2.2`，保留旧标签 `v0.2.0-dev` 和 `v0.2.1-dev`。
正式发布使用不带 `-dev` 的语义化版本号。
`scripts/Set-Version.ps1` 用来同步程序集、文件和产品版本；同时维护 `CHANGELOG.md`。
正式发布使用 `scripts/Package-Release.ps1`，要求已提交且干净的工作区，并在发布成功后
创建本地版本标签。开发版标签不表示已经完成正式发布回归。
用户配置和 API 密钥不进入版本库或发布包。当前没有远程备份；本地 Git 无法防止整块磁盘损坏。
公开更新文件发布到 `https://github.com/gefa250/Sharkey-Releases`；该仓库只存放发布说明和二进制资源，不上传本源码仓库。
