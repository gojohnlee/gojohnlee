using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;

namespace ValheimHelper
{
    public sealed partial class GatherJob
    {
        // ---- 사냥 (⑧) ----

        /// <summary>60m 안에서 그 물건을 떨어뜨리는 사냥감 중 가장 가까운 것(혼자 상대해도 되는 것만). 찾으면 m_prey에 둔다.</summary>
        bool FindPrey()
        {
            Vector3 me = m_npc.transform.position;
            Character best = null;
            float bestD = PreySight;
            bool canShoot = !m_bowFailed && m_npc.ShootingBow() != null;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || c.IsTamed() || c is WorkerNpc || !Prey.Is(Item, c, canShoot)) continue;
                if (m_huntSkip.TryGetValue(c, out float until) && Time.time < until) continue;
                Vector3 p = c.transform.position;
                float d = Utils.DistanceXZ(p, me);
                if (d >= bestD || Mathf.Abs(p.y - me.y) > 15f || Avoided(p)) continue;
                if (!m_ai.Guard.SafePrey(c, out Character pack))
                {
                    m_huntSkip[c] = Time.time + 120f;
                    if (m_hardPrey == null || m_hardPrey.IsDead() || d < Utils.DistanceXZ(m_hardPrey.transform.position, me)) m_hardPrey = c;
                    if (pack != null && m_packFoe == null)
                    {
                        m_packFoe = Localization.instance.Localize(pack.m_name);
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {Utils.GetPrefabName(c.gameObject)} 혼자면 되는데 둘레 무리({Utils.GetPrefabName(pack.gameObject)} 등) 때문에 안 됨");
                    }
                    else if (pack == null && m_strongFoe == null)
                    {
                        m_strongFoe = Localization.instance.Localize(c.m_name);
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {Utils.GetPrefabName(c.gameObject)} 혼자 잡기엔 너무 셈 — 안 덤빔");
                    }
                    continue;
                }
                best = c;
                bestD = d;
            }
            m_prey = best;
            m_huntArmed = false;
            m_preyShots = 0;
            if (best != null) Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {Utils.GetPrefabName(best.gameObject)} {bestD:0}m 앞");
            return best != null;
        }

        /// <summary>
        /// 혼자면 못 잡을 사냥감(무리 속·센 것): 호위병과 둘이면 잡을 수 있으면 부른다(사용자 결정 2026-10-26 — 일꾼끼리는 안 부름).
        /// 호위병이 쉬면 와서 같이(true — 올 때까지 기다렸다가 다시 찾음), 바쁘면 남은 양을 "호위병이 쉴 때" 대기열에 넣고 그만둠(m_guardQueued, 계획 단계는 그냥 그만둠).
        /// 호위병이 이미 곁(20m)이면 셈에 들어 있으니 둘이도 안 되는 것. 이 일에 한 번만.
        /// </summary>
        bool CallGuardForPrey()
        {
            if (m_mateCalled || m_ai.EscortName != null) return false;
            m_mateCalled = true;
            WorkerNpc guard = Departures.GuardOf();
            if (guard == null || m_hardPrey == null || m_hardPrey.IsDead()) return false;
            float extra = Vector3.Distance(guard.transform.position, m_npc.transform.position) <= Guard.AllyRange ? 0f : Guard.HitOf(guard);
            if (extra <= 0f || !m_ai.Guard.SafePrey(m_hardPrey, out _, extra))
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 호위병 {guard.NpcName}과 둘이도 {Utils.GetPrefabName(m_hardPrey.gameObject)}는 무리");
                return false;
            }
            var args = new Dictionary<string, string> { ["partner"] = guard.NpcName, ["item"] = ItemLabel(Item), ["foe"] = m_packFoe ?? m_strongFoe ?? "" };
            if (Departures.GoWithGuard(m_npc) != null)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 혼자면 안 되는 {Utils.GetPrefabName(m_hardPrey.gameObject)} → 호위병 {guard.NpcName} 불러 같이 잡음");
                m_npc.Say("preyMate", args);
                m_packFoe = m_strongFoe = null;
                m_huntSkip.Clear();
                return true;
            }
            if (InPlan) return false;
            int left = Mathf.Max(1, Target - Delivered);
            var cmd = new Core.Chat.ChatCommand { Kind = Core.Chat.CommandKind.Item, Item = Item, Amount = left };
            m_ai.Queue.Add(WorkSaves.ToQueued(m_npc, WorkSaves.GatherItem(new Speaker("", ZDOID.None, ZDOID.None), cmd, ItemLabel(Item) + " " + left, ReadyWhen.GuardIdle)));
            m_guardQueued = true;
            Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 호위병이 바쁨 → 남은 {left}개는 호위병이 쉴 때 다시");
            return false;
        }

        /// <summary>위험한 곳(Guard가 피한 적 자리) 둘레 20m 안에 이 물건의 사냥감이 살아 있나. foe = 그 자리에서 가장 가까운 적 이름(대사용).</summary>
        bool PreyNear(Vector3 danger, out string foe)
        {
            foe = null;
            bool prey = false;
            float bestD = float.MaxValue;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || c.IsPlayer() || c is WorkerNpc || c.IsTamed()) continue;
                float d = Utils.DistanceXZ(c.transform.position, danger);
                if (d > 20f) continue;
                if (Prey.Is(Item, c, true)) prey = true;
                if (d < bestD && BaseAI.IsEnemy(m_npc, c)) { bestD = d; foe = Localization.instance.Localize(c.m_name); }
            }
            return prey && foe != null;
        }

        /// <summary>쫓아가 친다(Guard.UpdateFight와 같은 방식). 죽으면 떨어진 것을 줍고, 2분 안에 못 잡거나 체력이 40% 아래면 그만둔다.</summary>
        bool UpdateHunt(float dt)
        {
            if (m_prey != null && !m_prey.IsDead()) m_preyPos = m_prey.transform.position;
            if (m_prey == null || m_prey.IsDead())
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 잡음({m_stepTime:0}초)");
                m_prey = null;
                m_targetPos = m_preyPos;
                m_targetInfo = null;
                Go(Step.Collect);
                return true;
            }
            if (m_npc.GetHealth() < m_npc.GetMaxHealth() * HuntHurt)
            {
                m_huntSkip[m_prey] = float.MaxValue;
                m_prey = null;
                return GiveUp("huntHurt");
            }
            if (m_stepTime > HuntLimit)
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {HuntLimit:0}초 안에 못 잡아 포기");
                m_huntSkip[m_prey] = float.MaxValue;
                m_prey = null;
                Go(Step.Search);
                return true;
            }
            if (Prey.NeedsBow(m_prey)) return UpdateShoot(dt);
            if (!m_huntArmed || m_npc.HoldingBow()) { m_huntArmed = true; m_npc.HoldWeapon(); } // 없으면 맨손으로
            ItemDrop.ItemData weapon = m_npc.GetCurrentWeapon();
            float range = weapon != null ? Mathf.Clamp(weapon.m_shared.m_attack.m_attackRange, 1.5f, 3f) : 1.5f;
            float d = Utils.DistanceXZ(m_preyPos, m_npc.transform.position) - m_prey.GetRadius();
            if (d > range * 0.9f) m_ai.MoveToPoint(dt, m_preyPos, range * 0.8f + m_prey.GetRadius(), true);
            else
            {
                m_ai.StopMoving();
                m_ai.Face(m_prey.GetCenterPoint());
                if (!m_npc.InAttack() && m_ai.IsLookingAt(m_preyPos, 25f)) m_npc.StartAttack(m_prey, false);
            }
            return true;
        }

        int m_shots, m_preyShots;
        static int s_shotMask;

        /// <summary>사냥감의 실제 움직임(0.25초마다 자리 차이). GetVelocity는 가려는 속도라 비탈·막힘에 서 있어도 달리는 값이 나와 앞질러 쏜 화살이 땅에 박혔다(회차 175 vh_bowtest).</summary>
        Character m_velOf;
        Vector3 m_velPos, m_preyVel;
        float m_velAt;

        Vector3 PreyVelocity()
        {
            Vector3 now = m_prey.transform.position;
            if (m_velOf != m_prey) { m_velOf = m_prey; m_velPos = now; m_velAt = Time.time; m_preyVel = Vector3.zero; }
            else if (Time.time - m_velAt >= 0.25f) { m_preyVel = (now - m_velPos) / (Time.time - m_velAt); m_velPos = now; m_velAt = Time.time; }
            return m_preyVel;
        }

        /// <summary>
        /// 활 사냥(사슴·토끼, 도망만 함): 30m 안에서 가리는 것이 없으면 서서 활을 끝까지 당겨, 갈 곳을 앞질러(Archery.Aim) 쏜다.
        /// 멀거나 가리면 다가간다(당기는 동안은 못 뛴다 → 당김을 풀고). 화살이 떨어지면 그 짐승은 그만두고 근접 무기를 든다.
        /// </summary>
        bool UpdateShoot(float dt)
        {
            if (!m_npc.HoldingBow() && !m_npc.HoldBow())
            {
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 화살이 없어 그만둠(쏜 화살 {m_shots})");
                m_npc.StopDraw();
                m_huntSkip[m_prey] = float.MaxValue;
                m_prey = null;
                m_npc.HoldWeapon();
                Go(Step.Search);
                return true;
            }
            Vector3 eye = m_npc.GetEyePoint(), center = m_prey.GetCenterPoint();
            float d = Utils.DistanceXZ(m_preyPos, m_npc.transform.position);
            if (s_shotMask == 0) s_shotMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            bool clear = d <= Archery.ShootRange && !Physics.Linecast(eye, center, s_shotMask);
            if (!clear)
            {
                m_npc.StopDraw();
                m_ai.MoveToPoint(dt, m_preyPos, d <= Archery.ShootRange ? 4f : Archery.Approach, true); // 가리면 더 다가간다
                return true;
            }
            m_ai.StopMoving();
            Vector3 aim = center;
            ItemDrop.ItemData bow = m_npc.GetCurrentWeapon();
            if (ArrowFlight(bow, out float speed, out float gravity))
            {
                Vector3 rel = center - eye, v = PreyVelocity();
                if (Archery.Aim(rel.x, rel.y, rel.z, v.x, v.y, v.z, speed, gravity, out float ax, out float ay, out float az))
                    aim = eye + new Vector3(ax, ay, az);
            }
            m_ai.Face(aim);
            if (m_diagShot && bow.m_lastProjectile != null && bow.m_lastProjectile != m_diagPrev)
            {
                // 화살은 공격 애니메이션의 발사 순간에 생긴다
                m_diagShot = false;
                m_diagArrow = bow.m_lastProjectile;
                m_diagTracking = true;
                Plugin.Log.LogInfo($"DIAG 활: 눈 {eye} 겨눔 {aim} 보는 쪽 {m_npc.GetLookDir()} 몸 앞 {m_npc.transform.forward} 화살 {m_diagArrow.transform.position} 속력 {speed:0} 중력 {gravity:0.0} 사슴 속도 {m_preyVel} (가려는 {m_prey.GetVelocity()}) 실제 화살 속도 {(m_diagArrow.GetComponent<Projectile>() is Projectile pj ? pj.GetVelocity().magnitude.ToString("0.0") + " 저항 " + pj.m_drag : "?")}");
            }
            if (m_diagArrow != null) m_diagLast = m_diagArrow.transform.position;
            else if (m_diagTracking)
            {
                m_diagTracking = false;
                Plugin.Log.LogInfo($"DIAG 화살 사라짐: 마지막 {m_diagLast} 사슴 {center} 체력 {m_prey.GetHealth():0.0}/{m_prey.GetMaxHealth():0}");
            }
            if (m_releaseCheck && Time.time - m_releaseAt > 2f)
            {
                // 놓았는데 화살이 안 나갔다(공격 애니메이션의 발사 순간이 안 옴): 세 번 잇달면 이 일 동안 활을 안 쓴다
                m_releaseCheck = false;
                int now = Gear.CountArrows(m_npc.GetInventory(), bow);
                if (now >= m_releaseArrows)
                {
                    m_dryShots++;
                    Plugin.Log.LogInfo($"DIAG 활 안 나감 {m_dryShots}번째: 비틀 {m_npc.IsStaggering()} 공격 중 {m_npc.InAttack()} 손 {m_npc.HandsDiag()} 땅 {m_npc.IsOnGround()} 화살 {now}");
                    if (m_dryShots >= 3)
                    {
                        Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 활이 안 나가 이 일 동안 활은 안 씀");
                        m_bowFailed = true;
                        m_npc.StopDraw();
                        m_npc.HoldWeapon();
                        m_prey = null;
                        Go(Step.Search);
                        return true;
                    }
                }
                else m_dryShots = 0;
            }
            if (m_npc.InAttack()) return true;
            float draw = m_npc.DrawBow(dt);
            if (draw >= 1f && m_ai.IsLookingAt(aim, 10f) && m_npc.ReleaseBow())
            {
                m_shots++;
                Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: 활 쏨 {m_shots}번째, {d:0}m, 화살 {Gear.CountArrows(m_npc.GetInventory(), bow)}개 남음");
                if (m_shots <= 3) { m_diagShot = true; m_diagPrev = bow.m_lastProjectile; }
                m_releaseCheck = true;
                m_releaseAt = Time.time;
                m_releaseArrows = Gear.CountArrows(m_npc.GetInventory(), bow);
                if (++m_preyShots >= MissLimit && m_prey.GetHealth() >= m_prey.GetMaxHealth())
                {
                    Plugin.Log.LogInfo($"{m_npc.NpcName} 사냥 {Item}: {m_preyShots}발 쏘았는데 체력 그대로 → 그 짐승은 그만둠");
                    m_huntSkip[m_prey] = float.MaxValue;
                    m_prey = null;
                    Go(Step.Search);
                }
            }
            return true;
        }

        bool m_diagShot, m_diagTracking, m_releaseCheck, m_bowFailed;
        float m_releaseAt;
        int m_releaseArrows, m_dryShots;
        Vector3 m_diagLast;
        GameObject m_diagArrow, m_diagPrev;

        /// <summary>끝까지 당겼을 때 화살 속력과 중력(바닐라 Attack.ProjectileAttackTriggered: 활 값 + 화살 값, 화살의 Projectile.m_gravity).</summary>
        bool ArrowFlight(ItemDrop.ItemData bow, out float speed, out float gravity)
        {
            speed = 0f;
            gravity = 0f;
            if (bow == null) return false;
            ItemDrop.ItemData ammo = m_npc.GetAmmoItem();
            if (!Gear.IsArrowFor(ammo, bow)) ammo = m_npc.GetInventory().GetAmmoItem(bow.m_shared.m_ammoType);
            Attack a = bow.m_shared.m_attack;
            speed = a.m_projectileVel;
            GameObject proj = a.m_attackProjectile;
            if (ammo != null)
            {
                speed += ammo.m_shared.m_attack.m_projectileVel;
                if (ammo.m_shared.m_attack.m_attackProjectile != null) proj = ammo.m_shared.m_attack.m_attackProjectile;
            }
            Projectile p = proj != null ? proj.GetComponent<Projectile>() : null;
            if (p != null) gravity = p.m_gravity;
            return speed > 0f;
        }

    }
}
