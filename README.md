# Survivalcraft Mech Defense

生存战争 API 1.9.3.1 / .NET 10 模组。

当前版 0.4.1：《明日方舟：终末地》全部 19 种防御塔。模型是 `E:/projects/endfield-turret-model`
的自制方块模型和自绘原色贴图，交给游戏原生的模型渲染（光照、雾、阴影、实例化）；
特效是自制像素风；音效是终末地原始音频。0.3.1 的闪烁、炮管穿模、卡顿，以及 0.4.0 的炮管透明、
接缝透光、物品图标太平的根因和修法、构建与验证步骤见 [docs/TOWERS.md](docs/TOWERS.md)。

早期样例的《毁灭枪手Σ》机甲和敌兵仍保留，旧存档可继续使用。

原始安装包、游戏资源、外部提取工具和构建产物保存在被 Git 忽略的位置（`local/`、`dist/`、
`Mod/Assets/Imported/`、`Mod/Assets/Towers/`）。来源路径与本机配置见 local/config.json，
进度及验证边界见 docs/STATUS.md。
