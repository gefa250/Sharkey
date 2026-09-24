# 鲨译 Sharkey

鲨译是一个适用于 Windows 10/11 x64 的桌面翻译工具。选中文字后按 `F8` 阅读译文，按 `F9` 框选屏幕文字并翻译；只有触发快捷键时才会工作。它也提供 AI 辅助的外贸沟通窗口。

> [下载可运行版本](https://github.com/gefa250/Sharkey-Releases/releases/latest) · [查看更新记录](CHANGELOG.md) · [报告问题](https://github.com/gefa250/Sharkey/issues)

当前源码版本为 `0.2.4`；公开下载页上的最新版可能稍早。此仓库存放源码，`Sharkey-Releases` 存放可运行程序及自动更新文件。

## 开始使用

1. 在[下载页](https://github.com/gefa250/Sharkey-Releases/releases/latest)获取 `Sharkey-win-x64.exe`，运行后可在系统托盘找到鲨鱼图标。
2. 选中一段文字，按 `F8`。初次使用可在“设置 → 翻译”选择 Google 免费或 Microsoft 免费，无需 API Key。
3. 需要截图翻译时，在“设置 → 翻译 → AI 模型”配置 DeepSeek 的 API 地址与 Key，然后在“设置 → OCR”启用 DeepSeek Vision。按 `F9` 框选文字即可识别并翻译。截图会发送到所配置的视觉模型服务。
4. 需要 AI 拟写客户回复时，配置可用的 AI 模型，然后按 `F7` 打开外贸沟通助手。

翻译目标默认使用智能模式：中文译成英文，其他语言译成简体中文；也可以在设置中指定固定目标语言。免费翻译依赖非官方网页接口，服务方可能调整或限制访问。

## 快捷键与窗口

| 默认键 | 功能 |
| --- | --- |
| `F7` | 外贸沟通助手：填写想法、客户消息和截图，生成可编辑的回复或仅获取沟通建议 |
| `F8` | 翻译当前选中的文字，显示紧凑的译文卡片 |
| `F9` | 框选屏幕区域，识别文字后翻译；原文可以修改并重新翻译 |
| `F10` | 打开设置 |

在“设置 → 快捷键”中可以直接按键录制自定义组合键。浮窗支持 `Esc` 收起、窗外点击收起、图钉固定、拖动和缩放。F9 工作区还可以切换上下或左右布局，并拖动原文与译文之间的分隔条。长文本可以滚动查看和选择复制。

## 翻译与 AI 配置

- **免费翻译**：Google 免费、Microsoft 免费，无需账号或密钥。
- **官方 API**：Google Cloud Translation、Microsoft Translator，需填写相应的 Key。
- **AI 模型**：DeepSeek、MiMo、Qwen 和自定义服务。每个供应商分别保存 Base URL、模型、接口协议和 Key；支持兼容 OpenAI Chat Completions 或 Anthropic Messages 的接口。
- **截图 OCR**：使用 DeepSeek Vision 转录图片，再由当前翻译引擎翻译。OCR 结果可以在翻译前后核对、编辑和重新翻译。

浏览某个服务的配置不会自动切换翻译引擎。点击“设为当前引擎”并“保存并应用”后，F8/F9 才会使用新选择。AI 模型和 OCR 的配置是分开的：即使 F8 使用免费翻译，F9 仍需可用的 DeepSeek Vision 配置。

F7 可以根据要点、客户消息及最多五张图片拟写回复，也能只给沟通建议。支持多轮调整、回复语言选择、编辑和复制。F9 的“文本工具”还提供 PDF 断行整理；译文中的数字与型号差异会给出核对提示。这些提示不能代替人工检查。

## 数据与网络

- API Key 保存在当前 Windows 用户的本地配置中，由 Windows DPAPI 加密；不会随源码仓库或发布包上传。
- F8 的免费及官方翻译、AI 翻译会向所选服务发送待翻译文字。F9 截图和 F7 附加图片会发送到所配置的视觉模型服务。
- 最近翻译、沟通会话和翻译缓存只保存在本次运行的内存中，退出后清空。截图在内存中编码，不写入翻译历史。
- 程序可手动检查 GitHub Releases 更新，并每天最多后台检查一次；下载更新时会校验 SHA-256。

## 从源码构建

项目使用 C#、WPF 和 .NET Framework，目标为 Windows x64。开发机需要对应的 .NET Framework 引用程序集及 MSBuild。现有脚本会生成单个 EXE：

```bat
build.cmd
test.cmd
```

输出位于 `bin\Release\Sharkey.exe`。如果该文件正被运行中的鲨译占用，可以指定另一个输出目录：

```bat
build.cmd bin\Preview\
test.cmd bin\Preview\Sharkey.exe
```

发布版本由 `VersionInfo.cs` 管理，打包流程见 [开发说明](DEVELOPMENT.md)。本机配置、密钥、构建输出和日志由 `.gitignore` 排除。需要反馈问题时，可以附上 Windows 版本、鲨译版本、复现步骤及“设置 → 常规”中的诊断摘要；提交日志前请自行检查其中的个人信息。

源码采用 [MIT 许可证](LICENSE)。欢迎通过 Issue 反馈问题或提交 Pull Request。
