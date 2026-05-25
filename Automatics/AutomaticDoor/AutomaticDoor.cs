using System;
using System.Collections.Generic;
using ModUtils;
using UnityEngine;

namespace Automatics.AutomaticDoor
{
    [DisallowMultipleComponent]
    internal sealed class AutomaticDoor : MonoBehaviour
    {
        private const float PredictionLookAheadSeconds = 0.35f;
        private const float MaxPredictionDistance = 1.5f;
        private const float MinPredictionSpeed = 1f;
        private const float MinApproachDot = 0.35f;
        private const float CloseGracePeriod = 0.35f;
        private const string PrivateAreaAllAreasField = "m_allAreas";
        private const string PrivateAreaPieceField = "m_piece";
        private const string PrivateAreaZNetViewField = "m_nview";

        private static readonly Lazy<int> LazyPieceMask;
        private static readonly IList<AutomaticDoor> AllInstance;

        private static int PieceMask => LazyPieceMask.Value;

        private Door _door;
        private Transform _transform;
        private ZNetView _zNetView;
        private bool _allowAutomaticDoor;
        private bool _allowAutomaticDoorDirty;
        private float _lastAutomaticOpenTime;

        public static bool HasRegisteredDoor => AllInstance.Count > 0;

        static AutomaticDoor()
        {
            LazyPieceMask = new Lazy<int>(() =>
                LayerMask.GetMask("Default", "static_solid", "Default_small", "piece",
                    "piece_nonsolid", "terrain", "vehicle"));
            AllInstance = new List<AutomaticDoor>();
        }

        private void Awake()
        {
            _door = GetComponent<Door>();
            _transform = transform;
            Objects.GetZNetView(_door, out _zNetView);

            _allowAutomaticDoorDirty = true;
            AllInstance.Add(this);
        }

        private void OnDestroy()
        {
            AllInstance.Remove(this);

            _zNetView = null;
            _transform = null;
            _door = null;
        }

        public static void MarkAllowAutomaticDoorDirty()
        {
            foreach (var automaticDoor in AllInstance)
                if (automaticDoor)
                    automaticDoor._allowAutomaticDoorDirty = true;
        }

        public static void TryOpenNearby(Player player, Vector3 velocity)
        {
            if (!player) return;

            var openRange = Config.DistanceForAutomaticOpening;
            if (openRange <= 0f) return;

            var searchRange = openRange + GetPredictionDistance(velocity);
            var searchRangeSquared = searchRange * searchRange;

            for (var index = AllInstance.Count - 1; index >= 0; index--)
            {
                var automaticDoor = AllInstance[index];
                if (!IsRegistered(automaticDoor))
                {
                    AllInstance.RemoveAt(index);
                    continue;
                }

                automaticDoor.TryOpen(player, velocity, searchRangeSquared, openRange);
            }
        }

        public static void TryCloseDoors()
        {
            var closeRange = Config.DistanceForAutomaticClosing;
            if (closeRange <= 0f) return;

            var holdOpenRange = Config.IntervalToOpen >= 0.1f
                ? Mathf.Max(closeRange, Config.DistanceForAutomaticOpening)
                : closeRange;
            var players = Player.GetAllPlayers();
            for (var index = AllInstance.Count - 1; index >= 0; index--)
            {
                var automaticDoor = AllInstance[index];
                if (!IsRegistered(automaticDoor))
                {
                    AllInstance.RemoveAt(index);
                    continue;
                }

                automaticDoor.TryClose(players, closeRange, holdOpenRange);
            }
        }

        private static bool IsRegistered(AutomaticDoor automaticDoor)
        {
            return automaticDoor && automaticDoor._door && automaticDoor._transform &&
                   automaticDoor._zNetView != null;
        }

        private static float GetPredictionDistance(Vector3 velocity)
        {
            var speed = velocity.magnitude;
            if (speed < MinPredictionSpeed) return 0f;

            return Mathf.Min(speed * PredictionLookAheadSeconds, MaxPredictionDistance);
        }

        private void TryOpen(Player player, Vector3 velocity, float searchRangeSquared,
            float openRange)
        {
            if (!IsValid()) return;
            if (!IsAllowAutomaticDoor()) return;
            if (IsDoorOpen() || !CanInteract(player)) return;
            if (!CanOpen(player)) return;

            var playerPosition = player.transform.position;
            var doorPosition = _transform.position;
            if ((doorPosition - playerPosition).sqrMagnitude > searchRangeSquared) return;
            if (!ShouldOpen(playerPosition, velocity, openRange)) return;
            if (IsExistsObstaclesBetweenTo(player)) return;

            if (_door.m_keyItem && Player.m_localPlayer == player)
                player.Message(MessageHud.MessageType.Center,
                    Localization.instance.Localize("$msg_door_usingkey",
                        _door.m_keyItem.m_itemData.m_shared.m_name));

            Reflections.InvokeMethod(_door, "Open", (playerPosition - doorPosition).normalized);
            _lastAutomaticOpenTime = Time.time;
        }

        private void TryClose(IEnumerable<Player> players, float closeRange, float holdOpenRange)
        {
            if (!IsValid()) return;
            if (!IsAllowAutomaticDoor()) return;
            if (!IsDoorOpen()) return;
            if (Time.time - _lastAutomaticOpenTime < CloseGracePeriod) return;

            var closestInteractablePlayer = default(Player);
            var closestDistanceSquared = float.MaxValue;
            var closeRangeSquared = closeRange * closeRange;
            var holdOpenRangeSquared = holdOpenRange * holdOpenRange;
            var doorPosition = _transform.position;

            foreach (var player in players)
            {
                if (!player) continue;

                var distanceSquared = (player.transform.position - doorPosition).sqrMagnitude;
                if (distanceSquared <= closeRangeSquared)
                    return;

                var canInteract = CanInteract(player);
                if (distanceSquared <= holdOpenRangeSquared &&
                    canInteract &&
                    CanOpen(player) &&
                    !IsExistsObstaclesBetweenTo(player))
                    return;

                if (!canInteract) continue;
                if (distanceSquared >= closestDistanceSquared) continue;

                closestDistanceSquared = distanceSquared;
                closestInteractablePlayer = player;
            }

            if (!closestInteractablePlayer) return;

            Reflections.InvokeMethod(_door, "Open",
                (closestInteractablePlayer.transform.position - doorPosition).normalized);
        }

        private bool IsAllowAutomaticDoor()
        {
            if (!_allowAutomaticDoorDirty) return _allowAutomaticDoor;

            _allowAutomaticDoor = Logics.IsAllowAutomaticDoor(_door);
            _allowAutomaticDoorDirty = false;
            return _allowAutomaticDoor;
        }

        private bool IsValid()
        {
            return Objects.HasValidOwnership(_zNetView);
        }

        private bool IsDoorOpen()
        {
            return _zNetView.GetZDO().GetInt("state") != 0;
        }

        private bool CanInteract(Player player)
        {
            if (_door.m_checkGuardStone && !CheckWardAccess(player, _transform.position))
                return false;
            return Reflections.InvokeMethod<bool>(_door, "CanInteract");
        }

        private static bool CheckWardAccess(Player player, Vector3 point)
        {
            if (!player) return false;

            var areas = Reflections.GetStaticField<PrivateArea, List<PrivateArea>>(PrivateAreaAllAreasField);
            if (areas == null) return false;

            var allowed = false;
            var foundBlockedArea = false;
            foreach (var area in areas)
            {
                if (!IsEnabled(area) || !IsInside(area, point, 0f)) continue;

                if (HasPlayerAccess(area, player))
                    allowed = true;
                else
                    foundBlockedArea = true;
            }

            return allowed || !foundBlockedArea;
        }

        private static bool IsEnabled(PrivateArea area)
        {
            if (!area) return false;

            var zNetView = Reflections.GetField<ZNetView>(area, PrivateAreaZNetViewField);
            return zNetView != null &&
                   zNetView.IsValid() &&
                   zNetView.GetZDO().GetBool(ZDOVars.s_enabled);
        }

        private static bool IsInside(PrivateArea area, Vector3 point, float radius)
        {
            return Utils.DistanceXZ(area.transform.position, point) < area.m_radius + radius;
        }

        private static bool HasPlayerAccess(PrivateArea area, Player player)
        {
            var playerId = player.GetPlayerID();
            if (playerId == 0L) return false;

            var piece = Reflections.GetField<Piece>(area, PrivateAreaPieceField);
            if (piece && piece.GetCreator() == playerId)
                return true;

            var zNetView = Reflections.GetField<ZNetView>(area, PrivateAreaZNetViewField);
            if (zNetView == null || !zNetView.IsValid()) return false;

            var zdo = zNetView.GetZDO();
            var permittedCount = zdo.GetInt(ZDOVars.s_permitted);
            for (var index = 0; index < permittedCount; index++)
                if (zdo.GetLong("pu_id" + index, 0L) == playerId)
                    return true;

            return false;
        }

        private bool CanOpen(Player player)
        {
            return !_door.m_keyItem || Reflections.InvokeMethod<bool>(_door, "HaveKey", player);
        }

        private bool ShouldOpen(Vector3 playerPosition, Vector3 velocity, float openRange)
        {
            var toDoor = _transform.position - playerPosition;
            var openRangeSquared = openRange * openRange;
            if (toDoor.sqrMagnitude <= openRangeSquared)
                return true;

            var predictionDistance = GetPredictionDistance(velocity);
            if (predictionDistance <= 0f)
                return false;

            var speed = velocity.magnitude;
            var moveDirection = velocity / speed;
            if (Vector3.Dot(moveDirection, toDoor.normalized) < MinApproachDot)
                return false;

            var predictedPosition = playerPosition + moveDirection * predictionDistance;
            return (_transform.position - predictedPosition).sqrMagnitude <= openRangeSquared;
        }

        private bool IsExistsObstaclesBetweenTo(Player player)
        {
            var from = Reflections.GetField<Collider>(player, "m_collider")?.bounds.center ??
                       player.m_eye.position;
            var to = _transform.position;

            if (!Physics.Linecast(from, to, out var hitInfo, PieceMask)) return false;

            var hitDoor = hitInfo.collider.GetComponentInParent<Door>();
            if (hitDoor) return hitDoor != _door;

            return hitInfo.collider.GetComponentInParent<Piece>();
        }
    }
}
