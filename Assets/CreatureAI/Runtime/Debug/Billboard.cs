using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace CreatureAI
{
    /// <summary>
    /// この GameObject を常に閲覧者(VRChat のローカルプレイヤーの頭)へ向ける簡易ビルボード。
    /// 状態表示ボードを常に読める向きにするために使う(Camera.main は Udon 非対応のため不使用)。
    /// 猫が回頭しても、子のボードはこれで常にこちらを向く。
    /// </summary>
    public class Billboard : UdonSharpBehaviour
    {
        [Tooltip("true なら水平回転のみ(上下に傾かない)。")]
        public bool lockPitch = true;

        void Update()
        {
            // Camera.main は Udon 非対応。VRChat のローカルプレイヤーの頭のみを使う。
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return; // エディタ非再生時など(プレイヤーがいない)

            Vector3 viewer = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            Vector3 dir = transform.position - viewer;
            if (lockPitch) dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}
