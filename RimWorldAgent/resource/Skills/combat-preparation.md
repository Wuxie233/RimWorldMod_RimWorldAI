---
name: combat-preparation
description: 战斗准备指南。在面临袭击、需要部署防御、组织战斗时激活。
---

# 战斗准备指南

## 袭击前准备
- 确认所有殖民者已征召（Draft）
- **设 1 倍速**：调用 toggle_pause(speed="normal")，便于精确指挥
- **近战人员**：用 get_recommended_apparel 找到最高护甲装备 → equip_pawn 强制穿戴（板甲/动力甲优先）
- **远程人员**：检查武器装备，确保弹药充足
- 将非战斗人员撤到安全区域
- 确认医疗物资（医药）位置

## 战斗响应流程（完整）

1. 收到袭击 → `get_game_speed` 确认减速状态 → 1 倍速手动暂停
2. `find_enemies(show_movement=true)` — 敌情+移动方向+ETA
3. `get_colonists` + `get_defense_status` — 评估己方战力
4. 装备分配：`equip_pawn(equipments=[...])` + `force_dress(equipments=[...])`
5. 征召战斗人员：`draft_pawn(drafted=true, colonist_ids=[...])`
6. `defend_position(action="list")` — 检查预设防御位
7. 近战堵门 + 远程 `shooting_position_grid` 扫描最优位
8. `move_pawn(moves=[...])` 全员就位
9. `force_attack(attacks=[{mode:melee},{mode:hold_position}...])` 开火
10. 战后：救治 → 解除征召 → 恢复工作

## 距离控制

### 远程站位
- 使用 `shooting_position_grid` 对目标周围区域扫评分
- 从 Top 5 结果中选择：掩体好 + 距离在射程 60-80% 处
- `move_pawn(moves=[...])` → `force_attack(mode="hold_position")` 站原地射击
- `hold_position` 模式：殖民者绝不移动，敌出射程 Job 自动结束，避免追出掩体

### 近战堵门
- `force_attack(mode="melee")` 冲向敌人贴脸
- 站在狭窄通道/门口，堵住敌人让后排远程在掩体后输出

### 风筝近战敌人
- 远程用 `shooting_position_grid` 选距敌人最近的掩护位
- 打完一轮 → 再次 `shooting_position_grid` 重新扫 → `move_pawn` 后撤
- 利用移动速度差保持距离

## 战斗位置选择

### 近战人员（穿最高护甲）
- **任务**：上前与敌人缠斗，拖住敌人保护后排
- **站位**：堵在狭窄通道或门口，迫使敌人排队交战
- **护甲优先**：穿板甲/动力甲，护甲值越高越好

### 远程人员（掩体后输出）
- **任务**：在掩体后安全输出伤害，不要上前近战
- **站位**：沙袋后、墙壁拐角、树木旁 — 任何有掩护的位置
- **集中目标**：集中火力逐个消灭，不要分散
- **批量工具**：`equip_pawn`/`force_dress`/`move_pawn`/`force_attack` 均支持批量，一次调用操作多个殖民者

### 通用原则
- 不要让远程人员脱离掩体冲锋
- 利用狭窄通道：让敌人排队进入射程
- 避免被包抄：注意侧翼和后方

## 武器射程
- 栓动步枪 (37格) — 远程精准火力
- 突击步枪 (31格) — 中远程持续输出
- 冲锋手枪 (19格) — 中距离灵活射击
- 猎枪 (16格) — 近距离高伤害
- 手榴弹 (6格) — 短距范围杀伤
- 近战武器 — 堵门肉盾

## 战斗速度
- **战斗期间始终 1 倍速**：toggle_pause(speed="normal")
- 1 倍速有充足时间观察战场变化、调整站位、救治倒下的殖民者
- 战斗完全结束后调用 toggle_pause(speed="superfast") 恢复 3 倍速

## 特定威胁应对
- 机械族：优先集火蜈蚣，EMP 手雷可瘫痪机械
- 虫族：保持距离，近战堵通道，后排射击
- 人类袭击：利用掩体优势，优先狙杀火箭兵
- 疯狂动物：近战殖民者排一线，远程站后排
- 围攻：优先摧毁迫击炮和弹药堆

## 战后处理
- 救治伤员 → 参考 [[medical-care]]
- 俘虏敌人 → 参考 [[pawn-interaction]]
- 清理尸体、修理建筑、补充物资

## 相关 Skill
- 装备优化 → [[equipment-optimization]]
- 医疗护理 → [[medical-care]]
- 角色交互 → [[pawn-interaction]]
