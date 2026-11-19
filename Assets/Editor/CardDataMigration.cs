
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CardDataMigration
{
    [MenuItem("Tools/カードデータ/名称変更データを一括移行")]
    static void Migrate()
    {
        if (!EditorUtility.DisplayDialog(
            "カードデータ一括移行",
            "CardDBBaseを継承した全カードデータを再保存します。\n\n" +
            "実行前にプロジェクトのバックアップを取ってください。",
            "移行する",
            "キャンセル"))
        {
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:CardDBBase");
        var paths = new List<string>();

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                EditorUtility.DisplayProgressBar(
                    "カードデータ検索中",
                    path,
                    (float)i / guids.Length
                );

                Object asset = AssetDatabase.LoadMainAssetAtPath(path);

                if (asset is CardDBBase)
                    paths.Add(path);
            }

            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "移行完了",
                    "対象となるカードデータが見つかりませんでした。",
                    "OK"
                );
                return;
            }

            EditorUtility.DisplayProgressBar(
                "カードデータ移行中",
                "アセットを再保存しています...",
                1f
            );

            AssetDatabase.ForceReserializeAssets(
                paths,
                ForceReserializeAssetsOptions.ReserializeAssets
            );

            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "移行完了",
                $"{paths.Count}個のカードデータアセットを再保存しました。",
                "OK"
            );
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
