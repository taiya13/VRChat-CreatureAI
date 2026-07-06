using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CreatureAI.EditorTools
{
    /// <summary>
    /// [PointTypeMask] が付いた PointType フィールドの描画(仕様 5章)。
    ///
    /// Unity 標準の EnumFlagsField は [InspectorName] の反映が Unity バージョンに
    /// よって不安定なため、それに頼らず、リフレクションで enum の各ビットと
    /// [InspectorName] を自前解決してマスクを組み立てる。これで将来の Unity
    /// バージョンアップに依存しない安定した日本語表示が得られる。
    ///
    /// 追加のフェイルセーフとして、種類が未選択(値 0)のときは HelpBox を出す。
    /// </summary>
    [CustomPropertyDrawer(typeof(PointTypeMaskAttribute))]
    public class PointTypeMaskDrawer : PropertyDrawer
    {
        private static string[] _cachedNames;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            string[] names = GetDisplayNames();
            float line = EditorGUIUtility.singleLineHeight;

            Rect maskRect = new Rect(position.x, position.y, position.width, line);

            EditorGUI.BeginProperty(position, label, property);
            int current = property.intValue;
            int updated = EditorGUI.MaskField(maskRect, label, current, names);
            if (updated != current) property.intValue = updated;
            EditorGUI.EndProperty();

            if (property.intValue == 0)
            {
                Rect warnRect = new Rect(position.x, position.y + line + 2f, position.width, line * 2f);
                EditorGUI.HelpBox(warnRect,
                    "種類が選択されていません。少なくとも 1 つ選択してください。",
                    MessageType.Warning);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            return property.intValue == 0 ? line * 3f + 2f : line;
        }

        /// <summary>
        /// ビット位置順(index i = 1&lt;&lt;i)の表示名配列を作る。
        /// [InspectorName] があればその日本語名、なければ enum メンバー名を使う。
        /// </summary>
        private static string[] GetDisplayNames()
        {
            if (_cachedNames != null) return _cachedNames;

            Type t = typeof(PointType);

            // 最上位ビットを求めて配列長を決める。
            int maxBit = 0;
            foreach (string n in Enum.GetNames(t))
            {
                int iv = (int)Enum.Parse(t, n);
                if (iv <= 0) continue;
                if (!IsSingleBit(iv)) continue;
                int bit = BitIndex(iv);
                if (bit > maxBit) maxBit = bit;
            }

            string[] names = new string[maxBit + 1];
            for (int i = 0; i < names.Length; i++) names[i] = "(未使用 " + i + ")";

            foreach (string n in Enum.GetNames(t))
            {
                FieldInfo fi = t.GetField(n);
                if (fi == null) continue;
                int iv = (int)fi.GetValue(null);
                if (iv <= 0) continue;          // None
                if (!IsSingleBit(iv)) continue; // 合成値は無視
                int bit = BitIndex(iv);

                string display = n;
                object[] attrs = fi.GetCustomAttributes(typeof(InspectorNameAttribute), false);
                if (attrs.Length > 0)
                {
                    display = ((InspectorNameAttribute)attrs[0]).displayName;
                }
                names[bit] = display;
            }

            _cachedNames = names;
            return names;
        }

        private static bool IsSingleBit(int v)
        {
            return v != 0 && (v & (v - 1)) == 0;
        }

        private static int BitIndex(int v)
        {
            int i = 0;
            while ((v >>= 1) != 0) i++;
            return i;
        }
    }
}
