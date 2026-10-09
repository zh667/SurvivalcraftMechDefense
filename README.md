# Survivalcraft Mech Defense

本地开发中的生存战争 API 1.9.3.1 / .NET 10 样例。

当前版 0.3.1：修复共面贴图闪烁、炮管左右绑定、死亡重复，增加固定方格占地和
Neorxna 中文信息兼容。三座铳械塔使用 `E:/projects/endfield-turret-model/v1` 自制模型与
贴图，绑定终末地提取的原始动画，保留终末地音效、特效和弹丸素材。
安装与验证边界见 [ENDFIELD_THREE.md](docs/ENDFIELD_THREE.md)。

目标：一座《明日方舟：终末地》炮塔、一台《毁灭枪手Σ》可驾驶机甲、一种敌方单位。
机甲独立放置于世界中，玩家进入/退出驾驶；炮塔部署在地面并自动攻击有视线的敌对生物。

原始安装包、游戏资源、外部提取工具和构建产物保存在被 Git 忽略的位置。
来源路径与本机配置见 local/config.json，进度及验证边界见 docs/STATUS.md。
