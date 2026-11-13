using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ProjectTabTitler
{
    private static bool isInitialized = false;
    private static Type projectBrowserType;
    private static FieldInfo titleContentField;
    private static MethodInfo getActiveFolderMethod;
    private static PropertyInfo isLockedProperty;

    [InitializeOnLoadMethod, ExecuteInEditMode]
    private static void Initialize()
    {
        if (isInitialized)
            return;

        projectBrowserType = Assembly.GetAssembly(typeof(EditorWindow)).GetTypes().First(x => x.Name == "ProjectBrowser");

        titleContentField = projectBrowserType.GetField("m_TitleContent", BindingFlags.Instance | BindingFlags.NonPublic);
        getActiveFolderMethod = projectBrowserType.GetMethod("GetActiveFolderPath", BindingFlags.NonPublic | BindingFlags.Instance);
        isLockedProperty = projectBrowserType.GetProperty("isLocked", BindingFlags.Instance | BindingFlags.NonPublic);

        EditorApplication.update += UpdateProjectTabTitles;

        isInitialized = true;
    }

    private static void UpdateProjectTabTitles()
    {
        var windows = Resources.FindObjectsOfTypeAll(projectBrowserType);
        string title;
        foreach (var window in windows)
        {
            // Revert to default Project tab
            if (!(bool)isLockedProperty.GetValue(window))
            {
                title = "Project";
            }
            // Set custom title
            else
            {
                // Path relative to Project folder
                title = getActiveFolderMethod.Invoke(window, new object[] { }).ToString();

                // Folder name only
                title = title.Split("/").Last();
            }

            GUIContent titleContent = new GUIContent(titleContentField.GetValue(window) as GUIContent);
            titleContent.text = title;

            titleContentField.SetValue(window, titleContent);
        }
    }
}