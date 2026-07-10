using SyncMaid.ViewModels;

namespace SyncMaid.UiTests.ViewModels;

public class EditorDialogViewModelTests
{
    [Theory]
    [InlineData(typeof(TaskEditorViewModel))]
    [InlineData(typeof(DestinationEditorViewModel))]
    public void Editors_share_the_generic_editor_dialog_base(Type editorType)
    {
        Assert.Equal(typeof(EditorDialogViewModel<>), editorType.BaseType!.GetGenericTypeDefinition());
    }
}
