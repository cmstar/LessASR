using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class CjkLatinSpacingNormalizerTests
{
    [Theory]
    [InlineData("T恤 V领 V領 POLO衫 AA制 4S店")]
    [InlineData("U盘 U盤 IC卡 IP卡 SIM卡 SD卡 TF卡 ATM机 ATM機 POS机 POS機 BP机 BP機 BB机 BB機")]
    [InlineData("A股 B股 H股 K线 K線")]
    [InlineData("B超 X光 X射线 X射線 A型血 B型血 AB型血 O型血 O型腿 X型腿")]
    [InlineData("卡拉OK K歌 阿Q Q版 Q弹 Q彈 三K党 三K黨 3K党 3K黨")]
    public void Normalize_PreservesCatalogSpellingsAndLetterCase(string input)
    {
        Assert.Equal(input, CjkLatinSpacingNormalizer.Normalize(input));
        var lowerCase = input.ToLowerInvariant();
        Assert.Equal(lowerCase, CjkLatinSpacingNormalizer.Normalize(lowerCase));
        foreach (var word in input.Split(' '))
        {
            var chineseContext = $"这是（{word}），请看{word}的说明。";
            Assert.Equal(chineseContext, CjkLatinSpacingNormalizer.Normalize(chineseContext));
        }
    }

    [Theory]
    [InlineData("买U盘和T恤", "买U盘和T恤")]
    [InlineData("关注A股和B股的K线", "关注A股和B股的K线")]
    [InlineData("做B超和X光检查", "做B超和X光检查")]
    [InlineData("这次AA制，去唱卡拉OK和K歌", "这次AA制，去唱卡拉OK和K歌")]
    [InlineData("读阿Q正传，买Q版玩偶", "读阿Q正传，买Q版玩偶")]
    [InlineData("插入SIM卡和SD卡，使用ATM机", "插入SIM卡和SD卡，使用ATM机")]
    [InlineData("血型是AB型血，汽车去4S店", "血型是AB型血，汽车去4S店")]
    [InlineData("买polo衫和v领上衣", "买polo衫和v领上衣")]
    [InlineData("使用U盤，查看K線，照X射線", "使用U盤，查看K線，照X射線")]
    [InlineData("U盘A股", "U盘A股")]
    [InlineData("喜欢Q弹口感", "喜欢Q弹口感")]
    [InlineData("T恤很好穿", "T恤很好穿")]
    [InlineData("唱卡拉OK", "唱卡拉OK")]
    [InlineData("T恤XL码", "T恤 XL 码")]
    [InlineData("Python阿Q", "Python 阿Q")]
    [InlineData("（T恤）与“卡拉OK”", "（T恤）与“卡拉OK”")]
    [InlineData("买 T恤和 U盘", "买 T恤和 U盘")]
    [InlineData("かなT恤한글", "かなT恤한글")]
    [InlineData("提到3K党和3k黨。", "提到3K党和3k黨。")]
    public void Normalize_ProtectsCommonMixedWords(string input, string expected)
    {
        Assert.Equal(expected, CjkLatinSpacingNormalizer.Normalize(input));
        Assert.Equal(expected, CjkLatinSpacingNormalizer.Normalize(expected));
    }

    [Theory]
    [InlineData("XT恤", "XT 恤")]
    [InlineData("XA股", "XA 股")]
    [InlineData("13K党", "13K 党")]
    [InlineData("某XSIM卡", "某 XSIM 卡")]
    [InlineData("卡拉OKR目标", "卡拉 OKR 目标")]
    [InlineData("阿QQ用户", "阿 QQ 用户")]
    [InlineData("使用AI模型和Windows系统", "使用 AI 模型和 Windows 系统")]
    [InlineData("T 恤和U  盘", "T 恤和 U  盘")]
    [InlineData("𠮷U盘和𠮷阿Q", "𠮷U盘和𠮷阿Q")]
    [InlineData("卡拉OK\u0301", "卡拉 OK\u0301")]
    public void Normalize_DoesNotProtectPartialLatinTokensOrRemoveExistingSpaces(string input, string expected)
    {
        Assert.Equal(expected, CjkLatinSpacingNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("使用Airdroid Business进行管理", "使用 Airdroid Business 进行管理")]
    [InlineData("使用café和Straße交流", "使用 café 和 Straße 交流")]
    [InlineData("使用cafe\u0301交流", "使用 cafe\u0301 交流")]
    [InlineData("これはWindowsです", "これは Windows です")]
    [InlineData("한국어English혼용", "한국어 English 혼용")]
    [InlineData("𠮷野家Windows版", "𠮷野家 Windows 版")]
    [InlineData("买T恤和t恤", "买T恤和t恤")]
    [InlineData("这件T恤XL码", "这件T恤 XL 码")]
    [InlineData("使用GPT4和3D模型", "使用 GPT4 和 3D 模型")]
    [InlineData("管理（EMM），让企业power up！", "管理（ EMM ），让企业 power up ！")]
    [InlineData("中文“混合English引文”", "中文“混合 English 引文”")]
    [InlineData("使用l’été和Alice’s电脑", "使用 l’été 和 Alice’s 电脑")]
    public void Normalize_SeparatesScriptsWithoutSplittingWords(string input, string expected)
    {
        Assert.Equal(expected, CjkLatinSpacingNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("纯中文测试。かなカナ한글")]
    [InlineData("Hello, world! Bonjour Straße café.")]
    [InlineData("使用  Windows\r\n继续\tLinux 测试")]
    [InlineData("中文\u3000English\u00a0中文")]
    [InlineData("共3个，2026年，版本2.0")]
    [InlineData("鲁迅没说过：“Good good study, day day up!”。")]
    [InlineData("他说「Bonjour, café!」然后离开")]
    [InlineData("路径 C:\\工作\\LessASR\\文件.txt 和 ./文件夹/test.txt")]
    [InlineData("访问 https://example.com/中文页面?a=中文A 和 test用户@example.com")]
    [InlineData("代码 `var 中文Name = 3;`\n```\nconst 中文Name = 2;\n```结束")]
    [InlineData("Hello—world… Bonjour!")]
    [InlineData("Alice’s computer, l’été, I’m ready.")]
    public void Normalize_PreservesExistingWhitespaceAndSingleScriptText(string input)
    {
        Assert.Equal(input, CjkLatinSpacingNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var once = CjkLatinSpacingNormalizer.Normalize("使用GPT4（EMM）和cafe\u0301！穿T恤。");

        Assert.Equal(once, CjkLatinSpacingNormalizer.Normalize(once));
    }

    [Fact]
    public void Normalize_PreservesUserExample()
    {
        const string text = "使用 Airdroid Business 进行企业设备管理（ EMM ），能显著提高生产力。让你的企业 power up ！\n\n鲁迅没说过：“Good good study, day day up!”。";

        Assert.Equal(text, CjkLatinSpacingNormalizer.Normalize(text));
    }
}
