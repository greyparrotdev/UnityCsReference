// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.Assertions;
using System.Collections.Generic;
using System.Linq;

namespace UnityEditor
{
    internal class DisplayedTask
    {
        public readonly Label nameLabel;
        public readonly Label progressLabel;
        public readonly Label descriptionLabel;
        public readonly Label elapsedTimeLabel;
        public readonly ProgressBar progressBar;
        public readonly Button cancelButton;
        public readonly Button deleteButton;
        public VisualElement progress;
        public bool isIndefinite;
        public bool isResponding;
        public float lastElapsedTime;

        public DisplayedTask(Label name, Label progress, Label description, Label elapsedTime, ProgressBar progressBar, Button cancelButton, Button deleteButton)
        {
            nameLabel = name;
            progressLabel = progress;
            this.elapsedTimeLabel = elapsedTime;
            this.descriptionLabel = description;
            this.progressBar = progressBar;
            this.cancelButton = cancelButton;
            this.deleteButton = deleteButton;
            this.progress = this.progressBar.Q(null, "unity-progress-bar__progress");
            isIndefinite = false;
            isResponding = true;
        }

        public void SetIndefinite(bool indefinite)
        {
            if (indefinite)
            {
                var progressTotalWidth = float.IsNaN(progressBar.worldBound.width) ? 146 : progressBar.worldBound.width;
                var barWidth = progressTotalWidth * 0.2f;
                if (indefinite != isIndefinite)
                {
                    progress.AddToClassList("unity-progress-bar__progress__full");
                    progress.style.left = 0;
                }
                progress.style.width = barWidth;
                var halfBarWidth = barWidth / 2.0f;
                var cos = Mathf.Cos((float)EditorApplication.timeSinceStartup * 2f);
                var rb = halfBarWidth;
                var re = progressTotalWidth - halfBarWidth;
                var scale = (re - rb) / 2f;
                var cursor = scale * cos;
                progress.style.left = cursor + scale;
            }
            else if (indefinite != isIndefinite)
            {
                progress.style.width = StyleKeyword.Auto;
                progress.style.left = 0;
                progress.RemoveFromClassList("unity-progress-bar__progress__full");
            }

            isIndefinite = indefinite;
        }

        public void SetProgressStyleFull(bool styleFull)
        {
            var isAlreadyFull = progress.ClassListContains("unity-progress-bar__progress__full");
            if (isAlreadyFull != styleFull)
            {
                if (styleFull)
                {
                    progress.AddToClassList("unity-progress-bar__progress__full");
                }
                else
                {
                    progress.RemoveFromClassList("unity-progress-bar__progress__full");
                }
            }
        }
    }

    internal class ProgressElement
    {
        private const string k_UxmlProgressPath = "UXML/ProgressWindow/ProgressElement.uxml";
        private const string k_UxmlSubTaskPath = "UXML/ProgressWindow/SubTaskElement.uxml";
        private static VisualTreeAsset s_VisualTreeBackgroundTask = null;
        private static VisualTreeAsset s_VisualTreeSubTask = null;

        private DisplayedTask m_MainTask;
        private List<ProgressItem> m_ProgressItemChildren;
        private List<DisplayedTask> m_SubTasks;
        private VisualElement m_Details;
        private ScrollView m_DetailsScrollView;
        private Toggle m_DetailsFoldoutToggle;

        public VisualElement rootVisualElement { get; }
        public ProgressItem dataSource { get; private set; }

        public ProgressElement(ProgressItem dataSource)
        {
            rootVisualElement = new TemplateContainer();
            if (s_VisualTreeBackgroundTask == null)
                s_VisualTreeBackgroundTask = EditorGUIUtility.Load(k_UxmlProgressPath) as VisualTreeAsset;

            var task = new VisualElement() { name = "Task" };
            rootVisualElement.Add(task);
            var horizontalLayout = new VisualElement();
            horizontalLayout.style.flexDirection = FlexDirection.Row;
            task.Add(horizontalLayout);
            m_DetailsFoldoutToggle = new Toggle() { visible = false };
            m_DetailsFoldoutToggle.AddToClassList("unity-foldout__toggle");
            m_DetailsFoldoutToggle.RegisterValueChangedCallback(ToggleDetailsFoldout);
            horizontalLayout.Add(m_DetailsFoldoutToggle);
            var parentTask = s_VisualTreeBackgroundTask.CloneTree();
            parentTask.name = "ParentTask";
            horizontalLayout.Add(parentTask);

            var details = new VisualElement() { name = "Details" };
            details.style.display = DisplayStyle.None;
            task.Add(details);

            m_Details = rootVisualElement.Q<VisualElement>("Details");
            m_DetailsScrollView = new ScrollView();
            m_Details.Add(m_DetailsScrollView);
            m_DetailsScrollView.AddToClassList("details-content");

            this.dataSource = dataSource;

            if (s_VisualTreeSubTask == null)
                s_VisualTreeSubTask = EditorGUIUtility.Load(k_UxmlSubTaskPath) as VisualTreeAsset;

            m_ProgressItemChildren = new List<ProgressItem>();
            m_SubTasks = new List<DisplayedTask>();

            m_MainTask = InitializeTask(dataSource, rootVisualElement);
        }

        internal ProgressItem GetSubTaskItem(int id)
        {
            foreach (var child in m_ProgressItemChildren)
            {
                if (child.id == id)
                    return child;
            }

            return null;
        }

        internal void CheckUnresponsive()
        {
            if (!dataSource.running)
            {
                return;
            }

            var taskElapsedTime = Mathf.Round(dataSource.elapsedTime);
            if (dataSource.finished || taskElapsedTime >= 2f)
            {
                if (m_MainTask.lastElapsedTime != taskElapsedTime)
                {
                    m_MainTask.lastElapsedTime = taskElapsedTime;
                    m_MainTask.elapsedTimeLabel.text = $"{taskElapsedTime:0} seconds";
                }
            }

            if (m_MainTask.isResponding != dataSource.responding)
            {
                UpdateResponsiveness(m_MainTask, dataSource);
            }

            for (int i = 0; i < m_ProgressItemChildren.Count; ++i)
            {
                if (m_SubTasks[i].isResponding != m_ProgressItemChildren[i].responding)
                {
                    UpdateResponsiveness(m_SubTasks[i], m_ProgressItemChildren[i]);
                }
            }
        }

        internal bool TryUpdate(ProgressItem op, int id)
        {
            if (dataSource.id == id)
            {
                dataSource = op;
                UpdateDisplay(m_MainTask, dataSource);
                return true;
            }
            else
            {
                for (int i = 0; i < m_ProgressItemChildren.Count; ++i)
                {
                    if (m_ProgressItemChildren[i].id == id)
                    {
                        m_ProgressItemChildren[i] = op;
                        UpdateDisplay(m_SubTasks[i], m_ProgressItemChildren[i]);
                        return true;
                    }
                }
            }
            return false;
        }

        internal bool TryRemove(int id)
        {
            for (int i = 0; i < m_ProgressItemChildren.Count; ++i)
            {
                if (m_ProgressItemChildren[i].id == id)
                {
                    m_DetailsScrollView.RemoveAt(i);
                    m_ProgressItemChildren.RemoveAt(i);
                    m_SubTasks.RemoveAt(i);
                    if (!m_ProgressItemChildren.Any())
                        rootVisualElement.Q<Toggle>().visible = false;
                    return true;
                }
            }
            return false;
        }

        internal void AddElement(ProgressItem item)
        {
            m_DetailsFoldoutToggle.visible = true;
            SubTaskInitialization(item);
            if (dataSource.running)
            {
                m_DetailsFoldoutToggle.SetValueWithoutNotify(true);
                ToggleDetailsFoldout(ChangeEvent<bool>.GetPooled(false, true));
            }
        }

        private static string ToPrettyFormat(TimeSpan span)
        {
            if (span == TimeSpan.Zero) return "00:00:00";
            return span.Days > 0 ? $"{span:dd\\.hh\\:mm\\:ss}" : $"{span:hh\\:mm\\:ss}";
        }

        private static void UpdateResponsiveness(DisplayedTask task, ProgressItem dataSource)
        {
            if (dataSource.responding && !task.isResponding)
            {
                task.descriptionLabel.text = dataSource.description;
                if (task.progress.ClassListContains("unity-progress-bar__progress__unresponding"))
                {
                    task.progress.RemoveFromClassList("unity-progress-bar__progress__unresponding");
                }
            }
            else if (!dataSource.responding && task.isResponding)
            {
                task.descriptionLabel.text = string.IsNullOrEmpty(dataSource.description) ? "(Not Responding)" : $"{dataSource.description} (Not Responding)";
                if (!task.progress.ClassListContains("unity-progress-bar__progress__unresponding"))
                {
                    task.progress.AddToClassList("unity-progress-bar__progress__unresponding");
                }
            }
            task.isResponding = dataSource.responding;
        }

        private void UpdateDisplay(DisplayedTask task, ProgressItem dataSource)
        {
            task.nameLabel.text = dataSource.name;

            task.descriptionLabel.text = dataSource.description;
            task.SetIndefinite(dataSource.indefinite);

            if (!dataSource.indefinite)
            {
                var p01 = Mathf.Clamp01(dataSource.progress);
                task.progressBar.value = p01 * 100.0f;
                if (task.progressLabel != null)
                    task.progressLabel.text = $"{Mathf.FloorToInt(p01 * 100.0f)}%";

                task.SetProgressStyleFull(dataSource.progress > 0.96f);
            }

            if (dataSource.status == ProgressStatus.Canceled)
            {
                task.descriptionLabel.text += " (Cancelled)";
                UpdateProgressCompletion(task, ProgressWindow.kCanceledIcon);
            }
            else if (dataSource.status == ProgressStatus.Failed)
            {
                task.descriptionLabel.text += " (Failed)";
                UpdateProgressCompletion(task, ProgressWindow.kFailedIcon);
            }
            else if (dataSource.status == ProgressStatus.Succeeded)
            {
                task.progressBar.value = 100;
                task.SetProgressStyleFull(true);
                UpdateProgressCompletion(task, ProgressWindow.kSuccessIcon);
                task.progressLabel.style.unityBackgroundImageTintColor = new StyleColor(Color.green);

                if (m_MainTask == task && m_DetailsFoldoutToggle.value)
                {
                    m_DetailsFoldoutToggle.value = false;
                }
            }

            task.deleteButton.style.display = !dataSource.running ? DisplayStyle.Flex : DisplayStyle.None;
            task.cancelButton.style.display = dataSource.running ? DisplayStyle.Flex : DisplayStyle.None;
            task.cancelButton.visible = dataSource.cancellable;
        }

        private static void UpdateProgressCompletion(DisplayedTask task, string iconName)
        {
            task.progressLabel.style.backgroundImage = EditorGUIUtility.LoadIcon(iconName);
            task.progressLabel.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            task.progressLabel.text = "";
            task.progressLabel.style.width = 30;
            task.progressLabel.style.height = ProgressWindow.kIconSize;
        }

        private void ToggleDetailsFoldout(ChangeEvent<bool> evt)
        {
            m_Details.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SubTaskInitialization(ProgressItem subTaskSource)
        {
            var parentElement = s_VisualTreeBackgroundTask.CloneTree();
            parentElement.name = "SubTask";

            DisplayedTask displayedSubTask = InitializeTask(subTaskSource, parentElement);

            m_ProgressItemChildren.Add(subTaskSource);
            m_SubTasks.Add(displayedSubTask);
            m_DetailsScrollView.Add(parentElement);
        }

        private DisplayedTask InitializeTask(ProgressItem progressItem, VisualElement parentElement)
        {
            var displayedTask = new DisplayedTask(
                parentElement.Q<Label>("BackgroundTaskNameLabel"),
                parentElement.Q<Label>("ProgressionLabel"),
                parentElement.Q<Label>("BackgroundTaskDescriptionLabel"),
                parentElement.Q<Label>("BackgroundTaskElapsedTimeLabel"),
                parentElement.Q<ProgressBar>("ProgressBar"),
                parentElement.Q<Button>("CancelButton"),
                parentElement.Q<Button>("DeleteButton")
            );
            Assert.IsNotNull(displayedTask.nameLabel);
            Assert.IsNotNull(displayedTask.descriptionLabel);
            Assert.IsNotNull(displayedTask.elapsedTimeLabel);
            Assert.IsNotNull(displayedTask.progressLabel);
            Assert.IsNotNull(displayedTask.progressBar);
            Assert.IsNotNull(displayedTask.cancelButton);
            Assert.IsNotNull(displayedTask.deleteButton);

            displayedTask.cancelButton.RemoveFromClassList("unity-text-element");
            displayedTask.cancelButton.RemoveFromClassList("unity-button");
            displayedTask.deleteButton.RemoveFromClassList("unity-text-element");
            displayedTask.deleteButton.RemoveFromClassList("unity-button");

            displayedTask.cancelButton.userData = progressItem;
            displayedTask.cancelButton.clickable.clickedWithEventInfo += CancelButtonClicked;

            displayedTask.deleteButton.userData = progressItem;
            displayedTask.deleteButton.clickable.clickedWithEventInfo += DeleteButtonClicked;

            UpdateDisplay(displayedTask, progressItem);
            UpdateResponsiveness(displayedTask, progressItem);
            return displayedTask;
        }

        private void CancelButtonClicked(EventBase obj)
        {
            var sender = obj.target as Button;
            var ds = sender?.userData as ProgressItem;
            if (ds != null)
            {
                var wasCancelled = ds.Cancel();
                if (wasCancelled)
                {
                    OnCancelled();
                }
            }
        }

        private void OnCancelled()
        {
            UpdateDisplay(m_MainTask, dataSource);
        }

        private static void DeleteButtonClicked(EventBase obj)
        {
            var sender = obj.target as Button;
            var ds = sender?.userData as ProgressItem;
            if (ds != null)
            {
                Progress.Clear(ds.id);
            }
        }
    }
}
