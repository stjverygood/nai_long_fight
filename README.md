# Nai Long Fight

一个使用 Godot 4 .NET / C# 制作的 3D 自动战斗观战游戏。选择 2–8 个势力后，会生成大范围随机起伏地形和中立水晶；每个势力拥有初始水晶，水晶持续生产该势力的兵。士兵自动占领水晶、交战并依据局势切换战术。失去全部水晶的势力会被淘汰。

## 运行

使用 Godot 4.7.2 .NET 版打开本目录中的 `project.godot`，运行主场景即可。

重新导出 Windows 版本：

```powershell
godot --headless --path . --export-release "Windows Desktop" build/NaiLongFight.exe
```

## 操作

- 选择势力数量后点击「开始自动战斗」
- 空格：暂停 / 继续
- 按住鼠标左键拖动：旋转视角
- WASD：移动视角
- 鼠标滚轮：缩放

士兵数达到每个势力 34 人后会停止继续增援；战斗持续到只剩一个势力存活。
