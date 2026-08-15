using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class VocabularyViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithNoProfiles_ShowsEmptyState()
    {
        var viewModel = new VocabularyViewModel(new StubVocabularyRepository());

        await viewModel.LoadAsync();

        Assert.Empty(viewModel.Profiles);
        Assert.False(viewModel.HasProfiles);
        Assert.False(viewModel.HasSelection);
        Assert.Equal("未使用词汇表", viewModel.ActiveVocabularyName);
    }

    [Fact]
    public async Task CreateAsync_FirstProfileBecomesActiveAndLaterProfileOnlyBecomesSelected()
    {
        var repository = new StubVocabularyRepository();
        var viewModel = new VocabularyViewModel(repository);
        await viewModel.LoadAsync();

        var first = await viewModel.CreateAsync("编程开发");
        var second = await viewModel.CreateAsync("日语学习");

        Assert.Equal(first.Id, repository.ActiveProfile?.Id);
        Assert.Equal(second.Id, viewModel.SelectedProfileId);
        Assert.Equal("编程开发", viewModel.ActiveVocabularyName);
        Assert.Equal(2, viewModel.Profiles.Count);
        Assert.True(viewModel.Profiles.Single(item => item.Id == first.Id).IsActive);
        Assert.False(viewModel.Profiles.Single(item => item.Id == second.Id).IsActive);
    }

    [Fact]
    public async Task SelectAsync_WithDirtyChanges_PreservesDraftAcrossSelectionsWithoutSaving()
    {
        var repository = StubVocabularyRepository.WithProfiles(
            Profile("编程", "LessASR", isActive: true),
            Profile("日语", "初音ミク"));
        var viewModel = new VocabularyViewModel(repository);
        await viewModel.LoadAsync();
        var target = viewModel.Profiles.Single(item => item.Name == "日语");
        viewModel.VocabularyText = "LessASR\nKubernetes";

        await viewModel.SelectAsync(target);

        Assert.Equal("日语", viewModel.SelectedName);
        Assert.Equal("LessASR", repository.Profiles.Single(item => item.Name == "编程").EntriesText);
        Assert.Equal(
            "2 项",
            viewModel.Profiles.Single(item => item.Name == "编程").EntryCountText);

        await viewModel.SelectAsync(viewModel.Profiles.Single(item => item.Name == "编程"));

        Assert.Equal("编程", viewModel.SelectedName);
        Assert.Equal("LessASR\nKubernetes", viewModel.VocabularyText);
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task SelectAsync_WithValidationError_PreservesInvalidDraftAcrossSelections()
    {
        var repository = StubVocabularyRepository.WithProfiles(
            Profile("编程", string.Empty, isActive: true),
            Profile("日语", string.Empty));
        var viewModel = new VocabularyViewModel(repository);
        await viewModel.LoadAsync();
        var invalidDraft = new string('词', WhisperVocabulary.MaxEntryCharacters + 1);
        viewModel.VocabularyText = invalidDraft;

        await viewModel.SelectAsync(viewModel.Profiles.Single(item => item.Name == "日语"));

        Assert.Equal("日语", viewModel.SelectedName);
        Assert.Null(viewModel.ValidationError);

        await viewModel.SelectAsync(viewModel.Profiles.Single(item => item.Name == "编程"));

        Assert.Equal(invalidDraft, viewModel.VocabularyText);
        Assert.NotNull(viewModel.ValidationError);
    }

    [Fact]
    public async Task SaveAsync_ValidatesUniqueNameAndNormalizesEntries()
    {
        var repository = StubVocabularyRepository.WithProfiles(
            Profile("编程", string.Empty, isActive: true),
            Profile("日语", string.Empty));
        var viewModel = new VocabularyViewModel(repository);
        await viewModel.LoadAsync();
        viewModel.SelectedName = "日语";

        Assert.Equal("词汇表名称不能重复。", viewModel.ValidationError);
        Assert.False(viewModel.CanSave);

        viewModel.SelectedName = "  开发术语  ";
        viewModel.VocabularyText = "  LessASR \r\n\r\nKubernetes\nLessASR ";
        await viewModel.SaveAsync();

        Assert.Equal("开发术语", viewModel.SelectedName);
        Assert.Equal("LessASR\nKubernetes", viewModel.VocabularyText);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task DraftNames_RemainUniqueAcrossUnsavedProfiles()
    {
        var repository = StubVocabularyRepository.WithProfiles(
            Profile("编程", string.Empty, isActive: true),
            Profile("日语", string.Empty));
        var viewModel = new VocabularyViewModel(repository);
        await viewModel.LoadAsync();
        viewModel.SelectedName = "共享草稿名";
        await viewModel.SelectAsync(viewModel.Profiles.Single(item => item.Name == "日语"));

        viewModel.SelectedName = "共享草稿名";

        Assert.Equal("词汇表名称不能重复。", viewModel.ValidationError);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public async Task ActivateAndDeactivate_ChangeActiveProfileWithoutChangingSelection()
    {
        var first = Profile("编程", string.Empty, isActive: true);
        var second = Profile("日语", "初音ミク");
        var repository = StubVocabularyRepository.WithProfiles(first, second);
        var viewModel = new VocabularyViewModel(repository);
        await viewModel.LoadAsync();
        await viewModel.SelectAsync(viewModel.Profiles.Single(item => item.Id == second.Id));
        viewModel.VocabularyText = "初音ミク\n重音テト";

        Assert.False(viewModel.CanActivate);
        await viewModel.ActivateSelectedAsync();
        Assert.Equal(first.Id, repository.ActiveProfile?.Id);

        await viewModel.SaveAsync();
        Assert.True(viewModel.CanActivate);
        await viewModel.ActivateSelectedAsync();

        Assert.Equal(second.Id, repository.ActiveProfile?.Id);
        Assert.Equal(
            "初音ミク\n重音テト",
            repository.Profiles.Single(item => item.Id == second.Id).EntriesText);
        Assert.Equal(second.Id, viewModel.SelectedProfileId);
        Assert.Equal("日语", viewModel.ActiveVocabularyName);

        viewModel.VocabularyText = "尚未保存的修改";
        await viewModel.DeactivateAsync();

        Assert.Null(repository.ActiveProfile);
        Assert.Equal(second.Id, viewModel.SelectedProfileId);
        Assert.Equal("未使用词汇表", viewModel.ActiveVocabularyName);
        Assert.Equal("尚未保存的修改", viewModel.VocabularyText);
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task DiscardChanges_RestoresLastSavedNameAndEntries()
    {
        var repository = StubVocabularyRepository.WithProfiles(
            Profile("编程", "LessASR", isActive: true));
        var viewModel = new VocabularyViewModel(repository);
        await viewModel.LoadAsync();
        viewModel.SelectedName = "临时名称";
        viewModel.VocabularyText = "临时词条";

        viewModel.DiscardChanges();

        Assert.Equal("编程", viewModel.SelectedName);
        Assert.Equal("LessASR", viewModel.VocabularyText);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task DeleteSelectedAsync_ActiveProfileLeavesNoneActiveAndSelectsNeighbor()
    {
        var first = Profile("编程", string.Empty, isActive: true);
        var second = Profile("日语", string.Empty);
        var repository = StubVocabularyRepository.WithProfiles(first, second);
        var viewModel = new VocabularyViewModel(
            repository,
            confirmDelete: _ => true);
        await viewModel.LoadAsync();

        await viewModel.DeleteSelectedAsync();

        Assert.Null(repository.ActiveProfile);
        Assert.Equal(second.Id, viewModel.SelectedProfileId);
        Assert.Equal("未使用词汇表", viewModel.ActiveVocabularyName);
    }

    private static VocabularyProfile Profile(
        string name,
        string entriesText,
        bool isActive = false)
    {
        var now = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);
        return new VocabularyProfile(Guid.NewGuid(), name, entriesText, isActive, now, now);
    }

    private sealed class StubVocabularyRepository : IVocabularyRepository
    {
        public List<VocabularyProfile> Profiles { get; } = [];

        public VocabularyProfile? ActiveProfile =>
            Profiles.SingleOrDefault(profile => profile.IsActive);

        public static StubVocabularyRepository WithProfiles(params VocabularyProfile[] profiles)
        {
            var repository = new StubVocabularyRepository();
            repository.Profiles.AddRange(profiles);
            return repository;
        }

        public Task<IReadOnlyList<VocabularyProfile>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<VocabularyProfile>>(Profiles.ToArray());
        }

        public Task<VocabularyProfile?> GetActiveAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ActiveProfile);
        }

        public Task<VocabularyProfile> CreateAsync(string name, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var profile = new VocabularyProfile(
                Guid.NewGuid(),
                name.Trim(),
                string.Empty,
                Profiles.Count == 0,
                now,
                now);
            Profiles.Add(profile);
            return Task.FromResult(profile);
        }

        public Task UpdateAsync(
            Guid id,
            string name,
            string entriesText,
            CancellationToken cancellationToken)
        {
            var index = Profiles.FindIndex(profile => profile.Id == id);
            Profiles[index] = Profiles[index] with
            {
                Name = name.Trim(),
                EntriesText = WhisperVocabulary.Parse(entriesText).NormalizedText,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(Guid? id, CancellationToken cancellationToken)
        {
            for (var index = 0; index < Profiles.Count; index++)
            {
                Profiles[index] = Profiles[index] with
                {
                    IsActive = id is not null && Profiles[index].Id == id
                };
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Profiles.RemoveAll(profile => profile.Id == id);
            return Task.CompletedTask;
        }
    }
}
