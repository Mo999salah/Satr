namespace Satr;

internal static partial class Ui
{
    public static bool Arabic = LoadLanguage();
    private static bool LoadLanguage()
    {
        try { return WorkspaceStore.Load(WorkspaceStore.StatePath).ArabicUi; } catch { return false; }
    }
    public static string L(string text) => Arabic && ArabicText.TryGetValue(text, out var translated) ? translated : text;
    private static readonly Dictionary<string, string> ArabicText = new()
    {
        ["Settings"] = "الإعدادات", ["Settings — Satr"] = "الإعدادات — سطر",
        ["Terminal"] = "الطرفية", ["Arabic & text"] = "العربية والنص", ["History"] = "السجل",
        ["Shortcuts"] = "الاختصارات", ["About"] = "حول البرنامج", ["Tools"] = "الأدوات",
        ["Diagnostics"] = "التشخيص", ["Workspace"] = "مساحة العمل",
        ["Apply"] = "تطبيق", ["Cancel"] = "إلغاء", ["Defaults"] = "الافتراضيات",
        ["Save"] = "حفظ", ["Close"] = "إغلاق", ["Search"] = "بحث", ["More"] = "المزيد",
        ["Commands"] = "الأوامر", ["New"] = "جديد", ["+   New session"] = "+   جلسة جديدة",
        ["Open project…"] = "فتح مشروع…", ["Restart"] = "إعادة التشغيل",
        ["Font settings apply to all sessions."] = "تُطبّق إعدادات الخط على جميع الجلسات.",
        ["Monospace font"] = "خط ثابت العرض", ["Font size"] = "حجم الخط",
        ["Arabic & mixed text"] = "العربية والنص المختلط", ["Smart RTL"] = "الاتجاه الذكي للعربية",
        ["Control how Arabic and English share a terminal line. Input sent to the tool stays unchanged."] = "تحكّم بعرض العربية والإنجليزية في السطر نفسه. لا تتغير المدخلات المرسلة للأداة.",
        ["Arrange mixed-script output for reading while keeping the terminal grid left-to-right."] = "ترتيب النص المختلط للقراءة مع إبقاء شبكة الطرفية من اليسار إلى اليمين.",
        ["Limit the output kept in memory for each session."] = "تحديد المخرجات المحفوظة في ذاكرة كل جلسة.",
        ["Scrollback lines"] = "عدد أسطر التمرير", ["Keyboard shortcuts"] = "اختصارات لوحة المفاتيح",
        ["Search shortcuts…"] = "بحث في الاختصارات…", ["No matching shortcuts"] = "لا توجد اختصارات مطابقة",
        ["Installed tools"] = "الأدوات المثبتة", ["Check again"] = "فحص مجددًا",
        ["Restart Satr if an installer changed PATH."] = "أعد تشغيل سطر إذا غيّر المثبّت مسارات PATH.",
        ["Copy log"] = "نسخ السجل", ["Refresh"] = "تحديث", ["Sidebar width"] = "عرض الشريط الجانبي",
        ["Hide sidebar"] = "إخفاء الشريط الجانبي", ["Unapplied changes"] = "تغييرات غير مطبّقة",
        ["Settings saved."] = "حُفظت الإعدادات.", ["Save failed — retry Apply"] = "تعذّر الحفظ — أعد المحاولة",
        ["Interface language — reopen Satr to apply throughout"] = "لغة الواجهة — أعد فتح سطر لتطبيقها بالكامل",
        ["Sidebar preferences are saved with your projects."] = "تُحفظ تفضيلات الشريط الجانبي مع مشاريعك.",
        ["Pin project"] = "تثبيت المشروع", ["Unpin project"] = "إلغاء تثبيت المشروع",
        ["Open folder"] = "فتح المجلد", ["Close project sessions"] = "إغلاق جلسات المشروع",
        ["Remove from recent projects"] = "إزالة من المشاريع الأخيرة",
        ["No open sessions"] = "لا توجد جلسات مفتوحة", ["Open a folder to work in"] = "افتح مجلدًا للعمل فيه",
        ["Copy selection"] = "نسخ التحديد", ["Paste into terminal"] = "لصق في الطرفية",
        ["Readable transcript"] = "النص المقروء", ["Paste image as file path"] = "لصق الصورة كمسار ملف",
        ["Copy folder path"] = "نسخ مسار المجلد", ["Reopen finished session"] = "إعادة فتح الجلسة المنتهية",
        ["Rename session"] = "تسمية الجلسة", ["Move session up"] = "نقل الجلسة لأعلى",
        ["Move session down"] = "نقل الجلسة لأسفل", ["Duplicate session in same folder"] = "تكرار الجلسة في المجلد نفسه",
        ["Start or reopen session"] = "تشغيل الجلسة أو إعادة فتحها", ["Force-kill session"] = "إنهاء الجلسة بالقوة",
        ["Bind conversation ID…"] = "ربط معرّف محادثة…", ["Bind conversation"] = "ربط محادثة",
        ["Copy all"] = "نسخ الكل", ["Wrap text"] = "التفاف النص", ["Previous"] = "السابق", ["Next"] = "التالي",
        ["Match case"] = "مطابقة حالة الأحرف", ["Search session output"] = "بحث في مخرجات الجلسة",
        ["Filter text…"] = "تصفية النص…", ["Project folder"] = "مجلد المشروع", ["Session type"] = "نوع الجلسة",
        ["Confirm"] = "تأكيد", ["Save a local text snapshot when saving the workspace"] = "حفظ نسخة نصية محلية عند حفظ مساحة العمل",
        ["Check the tools available to this Satr process. Installation and authentication stay with each tool."] = "فحص الأدوات المتاحة لسطر. التثبيت وتسجيل الدخول يتمان من خلال كل أداة.",
        ["Recent errors in this window. Review before sharing: messages can contain local paths."] = "آخر أخطاء هذه النافذة. راجعها قبل المشاركة؛ قد تتضمن مسارات محلية.",
        ["A project terminal for Arabic, mixed text, and command-line tools."] = "طرفية مشاريع للعربية والنص المختلط وأدوات سطر الأوامر.",
        ["Open a project folder, or start a shell in your current folder."] = "افتح مجلد مشروع، أو شغّل طرفية في المجلد الحالي.",
    };
}
