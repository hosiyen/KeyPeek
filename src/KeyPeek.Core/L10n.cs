namespace KeyPeek.Core;

public enum UiLanguage
{
    English,
    Vietnamese,
}

/// <summary>
/// UI language for everything KeyPeek says itself — window chrome, hints, the tray menu,
/// the overlay's labels. Shortcut DESCRIPTIONS come from the library data and are not
/// translated here: inventing 2,900 translations would ship wrong ones.
///
/// The English string is the key. That keeps every call site readable (the code says what
/// the UI says) and makes a missing translation fail soft: the user sees English, never a
/// resource token. The table is bidirectional so a rendered tree can be re-translated in
/// place when the user switches language — no restart.
/// </summary>
public static class L10n
{
    private static UiLanguage _language = UiLanguage.English;

    /// <summary>Raised after the language changes, so long-lived UI (tray menu, overlay)
    /// can re-render. Raised on the setter's thread.</summary>
    public static event Action? Changed;

    public static UiLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
                return;
            _language = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Map a settings value ("system" | "en" | "vi") to a language, using the OS
    /// display language for "system".</summary>
    public static UiLanguage Resolve(string? setting, string osTwoLetterLanguage) =>
        setting?.Trim().ToLowerInvariant() switch
        {
            "vi" => UiLanguage.Vietnamese,
            "en" => UiLanguage.English,
            _ => string.Equals(osTwoLetterLanguage, "vi", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.Vietnamese
                : UiLanguage.English,
        };

    /// <summary>Translate an English UI string into the current language. Unknown strings
    /// come back unchanged — English is always a safe answer.</summary>
    public static string T(string english) =>
        _language == UiLanguage.Vietnamese && Table.TryGetValue(english, out (string En, string Vi) pair)
            ? pair.Vi
            : english;

    /// <summary>For re-translating text already on screen: accepts a string in EITHER
    /// language and returns the current-language form, or null when the string is not one
    /// of ours (user content, key cap labels, app names…).</summary>
    public static string? TryLocalize(string text) =>
        Table.TryGetValue(text, out (string En, string Vi) pair)
            ? (_language == UiLanguage.Vietnamese ? pair.Vi : pair.En)
            : null;

    /// <summary>Every English key, for the tests.</summary>
    public static IEnumerable<string> EnglishKeys => Pairs.Select(p => p.En);

    public static IEnumerable<(string En, string Vi)> AllPairs => Pairs;

    // One entry per English string the UI can show. Keep the English side byte-identical
    // to the XAML/code literal — the lookup is exact-match.
    private static readonly (string En, string Vi)[] Pairs =
    {
        // ---- navigation & pages ----
        ("Home", "Trang chính"),
        ("Settings", "Cài đặt"),
        ("Shortcut library", "Thư viện phím tắt"),
        ("Conflicts", "Xung đột"),
        ("Search", "Tìm kiếm"),
        ("Search settings and shortcuts", "Tìm cài đặt và phím tắt"),

        // ---- Home ----
        ("None", "Không có"),
        ("Never", "Chưa lần nào"),
        ("Every chord resolves to one shortcut.", "Mỗi tổ hợp phím chỉ ứng với một phím tắt."),
        ("Chords Windows takes before the app ever sees them.",
            "Các tổ hợp bị Windows chặn trước khi ứng dụng kịp nhận."),
        ("View conflicts", "Xem xung đột"),
        ("Library updates", "Cập nhật thư viện"),
        ("Definitions are fetched over HTTPS and carry no data about you.",
            "Định nghĩa được tải qua HTTPS và không kèm bất kỳ dữ liệu nào về bạn."),
        ("KeyPeek has not looked for updated definitions yet.",
            "KeyPeek chưa lần nào kiểm tra bản cập nhật định nghĩa."),
        ("Check now", "Kiểm tra ngay"),
        ("Apps covered", "Ứng dụng được hỗ trợ"),
        ("{0} shortcuts, including the ones you added.",
            "{0} phím tắt, gồm cả những phím bạn tự thêm."),
        ("Browse library", "Xem thư viện"),
        ("Hold to reveal", "Giữ phím để hiện"),
        ("Hold any of them for {0} ms.", "Giữ một phím bất kỳ trong {0} ms."),
        ("Change triggers", "Đổi phím kích hoạt"),
        ("KeyPeek {0} · MIT licensed", "KeyPeek {0} · Giấy phép MIT"),
        ("Everything looks healthy.", "Mọi thứ đều ổn."),
        ("One thing needs a look.", "Có một mục cần xem lại."),
        ("Shortcut data derived from Microsoft PowerToys, MIT licensed.",
            "Dữ liệu phím tắt lấy từ Microsoft PowerToys, giấy phép MIT."),
        ("Keystrokes are never logged. KeyPeek only ever downloads two things, both read-only and carrying no user data: shortcut definitions, and each app's official logo from its own vendor.",
            "Phím gõ không bao giờ bị ghi lại. KeyPeek chỉ tải về đúng hai thứ, đều chỉ đọc và không kèm dữ liệu người dùng: định nghĩa phím tắt, và logo chính thức của từng ứng dụng từ chính hãng đó."),
        ("Open library folder", "Mở thư mục thư viện"),
        ("Open log", "Mở nhật ký"),
        ("just now", "vừa xong"),
        ("{0} minutes ago", "{0} phút trước"),
        ("{0} hours ago", "{0} giờ trước"),
        ("{0} days ago", "{0} ngày trước"),

        // ---- Settings: triggers & delay ----
        ("Trigger keys", "Phím kích hoạt"),
        ("Hold any of these keys to reveal the shortcut panel, pre-filtered to that key.",
            "Giữ một trong các phím này để hiện bảng phím tắt, đã lọc sẵn theo phím đó."),
        ("Shift is off by default: holding Shift while typing capitals is normal typing. Win and Alt holds are masked so releasing them doesn't open the Start menu or the app's menu bar.",
            "Shift tắt mặc định: giữ Shift để gõ chữ hoa là thao tác gõ bình thường. Giữ Win và Alt được che nên khi thả ra sẽ không mở menu Start hay thanh menu của ứng dụng."),
        ("Hold delay", "Thời gian giữ phím"),
        ("How long a trigger key must be held before the panel appears.",
            "Phải giữ phím kích hoạt trong bao lâu thì bảng mới hiện."),
        ("Hold Ctrl with this window focused to feel the timing.",
            "Giữ Ctrl khi cửa sổ này đang được chọn để cảm nhận độ trễ."),
        ("The same hold time applies to every trigger key.",
            "Thời gian giữ này áp dụng cho mọi phím kích hoạt."),
        ("Holding…", "Đang giữ…"),
        ("That's {0} ms — the panel would appear now.",
            "Đó là {0} ms — bảng sẽ hiện ra lúc này."),

        // ---- Settings: accent, motion, position ----
        ("Accent colour", "Màu nhấn"),
        ("Used for the held key, selected rows and links. By default KeyPeek borrows the colour you already chose in Windows.",
            "Dùng cho phím đang giữ, hàng được chọn và liên kết. Mặc định KeyPeek mượn màu bạn đã chọn trong Windows."),
        ("Indigo", "Chàm"),
        ("Violet", "Tím"),
        ("Teal", "Xanh cổ vịt"),
        ("Amber", "Hổ phách"),
        ("Motion", "Chuyển động"),
        ("The panel fades in over about a tenth of a second.",
            "Bảng hiện dần trong khoảng một phần mười giây."),
        ("Animate the panel", "Hiệu ứng khi mở bảng"),
        ("Turn this off if the panel ever looks like it stutters — it then appears instantly.",
            "Tắt đi nếu bảng có lúc bị giật — khi đó bảng sẽ hiện ngay lập tức."),
        ("High Contrast is on, so the panel appears instantly — a fade is a brief drop in contrast, which is the thing that mode exists to prevent.",
            "Chế độ Tương phản cao đang bật nên bảng hiện ngay lập tức — hiệu ứng mờ dần làm giảm tương phản trong chốc lát, đúng điều chế độ này sinh ra để tránh."),
        ("Panel position", "Vị trí bảng"),
        ("Where the panel appears on the screen you're working on.",
            "Bảng hiện ở đâu trên màn hình bạn đang làm việc."),
        ("Top", "Trên"),
        ("Center", "Giữa"),
        ("Bottom", "Dưới"),

        // ---- Settings: explore, suggestions ----
        ("Keyboard navigation", "Điều hướng bằng bàn phím"),
        ("By default KeyPeek only watches your keys — it never takes one. Turn this on to steer the panel from the keyboard.",
            "Mặc định KeyPeek chỉ quan sát phím — không bao giờ chiếm phím nào. Bật lên để điều khiển bảng bằng bàn phím."),
        ("Explore mode", "Chế độ khám phá"),
        ("While the panel is open: ↑↓ move, ←→ jump between groups, Enter runs the selected shortcut. Those keys go to KeyPeek instead of the app — only while the panel is on screen.",
            "Khi bảng đang mở: ↑↓ di chuyển, ←→ nhảy giữa các nhóm, Enter chạy phím tắt đang chọn. Các phím này đi vào KeyPeek thay vì ứng dụng — chỉ trong lúc bảng đang hiện."),
        ("Suggestions", "Gợi ý"),
        ("A short strip at the top of the panel with the shortcuts you reach for most.",
            "Một dải ngắn ở đầu bảng với các phím tắt bạn hay dùng nhất."),
        ("Show “Frequently used”", "Hiện mục “Hay dùng”"),
        ("Falls back to the curated picks until you've used a few.",
            "Dùng bộ chọn sẵn cho tới khi bạn đã dùng được một vài phím."),
        ("Learn from what I click here", "Học từ những gì tôi bấm ở đây"),
        ("Counts only rows clicked inside KeyPeek's panel. Your keystrokes are never read or recorded.",
            "Chỉ đếm các hàng được bấm trong bảng của KeyPeek. Phím bạn gõ không bao giờ bị đọc hay ghi lại."),
        ("Clear usage data", "Xoá dữ liệu sử dụng"),
        ("Nothing recorded yet.", "Chưa ghi nhận gì."),
        ("1 panel click recorded in usage.json.", "Đã ghi 1 lượt bấm vào usage.json."),
        ("{0} panel clicks recorded in usage.json.", "Đã ghi {0} lượt bấm vào usage.json."),

        // ---- Settings: appearance, language, behavior ----
        ("Appearance", "Giao diện"),
        ("Applies to the overlay and this window, instantly.",
            "Áp dụng ngay cho bảng phím tắt và cửa sổ này."),
        ("Theme", "Chủ đề"),
        ("Dark", "Tối"),
        ("Light", "Sáng"),
        ("Follow Windows", "Theo Windows"),
        ("Transparency", "Độ trong suốt"),
        ("0% is a solid panel; higher lets the app underneath show through.",
            "0% là bảng đặc; càng cao càng nhìn xuyên được ứng dụng bên dưới."),
        ("Language", "Ngôn ngữ"),
        ("KeyPeek's own text. Shortcut descriptions come from the library and stay in English.",
            "Phần chữ của chính KeyPeek. Mô tả phím tắt lấy từ thư viện nên vẫn là tiếng Anh."),
        ("Behavior", "Hành vi"),
        ("Start KeyPeek when I sign in", "Khởi động KeyPeek khi tôi đăng nhập"),
        ("Uses the current-user Run key. No service, no scheduled task.",
            "Dùng khoá Run của người dùng hiện tại. Không service, không tác vụ định thời."),
        ("Show over fullscreen apps", "Hiện đè lên ứng dụng toàn màn hình"),
        ("Off by default so games and video are never covered.",
            "Tắt mặc định để không bao giờ che game hay video."),

        // ---- Settings: updates, logos, exclusions ----
        ("Fetches corrected shortcut definitions from the community library. A read-only download; nothing about you or this PC is sent.",
            "Tải các định nghĩa phím tắt đã sửa từ thư viện cộng đồng. Chỉ tải xuống; không gửi đi bất cứ gì về bạn hay máy này."),
        ("Check automatically", "Tự động kiểm tra"),
        ("Every", "Mỗi"),
        ("days", "ngày"),
        ("Advanced", "Nâng cao"),
        ("Library source", "Nguồn thư viện"),
        ("Enter a valid http(s) URL.", "Nhập một URL http(s) hợp lệ."),
        ("Reset to default", "Về mặc định"),
        ("Download app logos", "Tải logo ứng dụng"),
        ("Fetches each app's official logo once, from that app's own vendor, and keeps it on this PC. KeyPeek ships no company logos of its own. Off: the list uses drawn icons and nothing is requested.",
            "Tải logo chính thức của mỗi ứng dụng đúng một lần, từ chính hãng đó, và giữ trên máy này. KeyPeek không đóng gói logo của công ty nào. Tắt: danh sách dùng icon tự vẽ và không gửi yêu cầu nào."),
        ("Last checked {0}", "Kiểm tra lần cuối: {0}"),
        ("Never checked yet", "Chưa kiểm tra lần nào"),
        ("Checking…", "Đang kiểm tra…"),
        ("Excluded apps", "Ứng dụng bị loại trừ"),
        ("The overlay never appears while these apps are focused.",
            "Bảng không bao giờ hiện khi các ứng dụng này đang được chọn."),
        ("Nothing excluded.", "Chưa loại trừ ứng dụng nào."),
        ("Add", "Thêm"),
        ("Pick running app…", "Chọn ứng dụng đang chạy…"),
        ("Remove", "Gỡ"),

        // ---- library page ----
        ("SYSTEM", "HỆ THỐNG"),
        ("APPS", "ỨNG DỤNG"),
        ("{0} apps · {1} shortcuts", "{0} ứng dụng · {1} phím tắt"),
        ("System-wide", "Toàn hệ thống"),
        ("Shown in every app, alongside that app's own shortcuts",
            "Hiện trong mọi ứng dụng, bên cạnh phím tắt riêng của ứng dụng đó"),
        ("My shortcuts", "Phím tắt của tôi"),
        ("Add or edit your own shortcuts for this app",
            "Thêm hoặc sửa phím tắt của riêng bạn cho ứng dụng này"),
        ("Open file", "Mở file"),
        ("Open this app's definition file in a text editor",
            "Mở file định nghĩa của ứng dụng này trong trình soạn thảo"),
        ("Reset", "Đặt lại"),
        ("Delete my overrides for this app", "Xoá các ghi đè của tôi cho ứng dụng này"),
        ("Remove your entries that shadow shipped ones for this app",
            "Gỡ các mục của bạn đang che mục có sẵn của ứng dụng này"),
        ("You haven't overridden anything for this app",
            "Bạn chưa ghi đè gì cho ứng dụng này"),
        ("Select an app", "Chọn một ứng dụng"),
        ("{0} shortcuts", "{0} phím tắt"),
        ("matches {0}", "khớp với {0}"),
        ("checked against {0}", "đã kiểm tra với {0}"),
        ("This definition has no shortcuts yet. Use “My shortcuts” to add some.",
            "Định nghĩa này chưa có phím tắt nào. Bấm “Phím tắt của tôi” để thêm."),
        ("Your override · {0}", "Ghi đè của bạn · {0}"),
        ("From the community library · {0}", "Từ thư viện cộng đồng · {0}"),
        ("Discovered from the app's own config · {0}",
            "Đọc từ cấu hình của chính ứng dụng · {0}"),
        ("Reload library", "Tải lại thư viện"),
        ("Shipped with KeyPeek", "Có sẵn trong KeyPeek"),
        ("Add app", "Thêm ứng dụng"),
        ("Edit in your library", "Sửa trong thư viện của bạn"),

        // ---- conflicts page ----
        ("Chords claimed in more than one place that could both apply to the same window. Two unrelated apps using the same chord is not a conflict.",
            "Các tổ hợp được khai ở nhiều nơi mà có thể cùng áp vào một cửa sổ. Hai ứng dụng không liên quan dùng chung một tổ hợp thì không phải xung đột."),
        ("No conflicts", "Không có xung đột"),
        ("Every chord in your library resolves to exactly one shortcut.",
            "Mỗi tổ hợp trong thư viện của bạn ứng với đúng một phím tắt."),
        ("App vs system-wide", "Ứng dụng và toàn hệ thống"),
        ("Two definitions match the same app", "Hai định nghĩa cùng khớp một ứng dụng"),
        ("The app's own shortcut usually wins while {0} is focused.",
            "Phím tắt riêng của ứng dụng thường thắng khi {0} đang được chọn."),
        ("Whichever definition loads first wins — give one a titleRegex to separate them.",
            "Định nghĩa nào nạp trước sẽ thắng — thêm titleRegex cho một bên để tách chúng."),

        // ---- search page ----
        ("Results for “{0}”", "Kết quả cho “{0}”"),
        ("No settings or shortcuts match that.", "Không có cài đặt hay phím tắt nào khớp."),
        ("Opacity / transparency", "Độ mờ / trong suốt"),
        ("Explore mode / keyboard navigation", "Chế độ khám phá / điều hướng bàn phím"),
        ("Start at sign-in", "Khởi động khi đăng nhập"),

        // ---- overlay ----
        ("Search actions…", "Tìm thao tác…"),
        ("FREQUENTLY USED", "HAY DÙNG"),
        ("SYSTEM-WIDE", "TOÀN HỆ THỐNG"),
        ("IN {0}", "TRONG {0}"),
        ("COMMON IN MOST APPS", "DÙNG CHUNG CHO HẦU HẾT ỨNG DỤNG"),
        ("No definition for {0} yet — showing shortcuts that work in most apps.",
            "Chưa có định nghĩa cho {0} — đang hiện các phím tắt dùng được trong hầu hết ứng dụng."),
        ("No shortcuts defined for this app yet.",
            "Ứng dụng này chưa có phím tắt nào được định nghĩa."),
        ("This app is running as administrator — Windows blocks click-to-run against it.",
            "Ứng dụng này đang chạy với quyền quản trị — Windows chặn việc bấm để chạy vào nó."),
        ("Create definition file", "Tạo file định nghĩa"),
        ("Pinned — type to search · Esc or a click outside closes",
            "Đã ghim — gõ để tìm · Esc hoặc bấm ra ngoài để đóng"),
        ("Click a shortcut to run it · hold more modifiers to narrow · Esc closes",
            "Bấm một phím tắt để chạy · giữ thêm phím bổ trợ để thu hẹp · Esc để đóng"),
        ("No shortcuts match the current filter.",
            "Không có phím tắt nào khớp bộ lọc hiện tại."),
        ("Desktop", "Màn hình nền"),
        ("checked against {0} · you're on {1}", "đã kiểm tra với {0} · bạn đang dùng {1}"),
        ("or", "hoặc"),
        ("Click to run", "Bấm để chạy"),
        ("The Office key on some Microsoft keyboards presses this whole combo for you.",
            "Phím Office trên một số bàn phím Microsoft bấm sẵn cả tổ hợp này cho bạn."),
        ("(display only — not sendable)", "(chỉ hiển thị — không gửi được)"),

        ("KeyPeek is already running — look for its icon in the system tray.",
            "KeyPeek đang chạy rồi — tìm biểu tượng của nó ở khay hệ thống."),
        ("PowerToys Shortcut Guide is also enabled on this PC. Two shortcut overlays will compete for the same job (especially the Win key).",
            "PowerToys Shortcut Guide cũng đang bật trên máy này. Hai bảng phím tắt sẽ giành nhau cùng một việc (nhất là phím Win)."),
        ("Turn off PowerToys' Shortcut Guide? (PowerToys may need a restart to notice. You can re-enable it any time in PowerToys Settings.)",
            "Tắt Shortcut Guide của PowerToys? (Có thể cần khởi động lại PowerToys. Bạn bật lại được bất cứ lúc nào trong PowerToys Settings.)"),

        // ---- tray ----
        ("Open KeyPeek", "Mở KeyPeek"),
        ("Open settings file", "Mở file cài đặt"),
        ("Exit", "Thoát"),
        ("KeyPeek — hold {0} to see shortcuts", "KeyPeek — giữ {0} để xem phím tắt"),
        ("KeyPeek library reloaded", "Đã tải lại thư viện KeyPeek"),
        ("{0} apps, {1} shortcuts.", "{0} ứng dụng, {1} phím tắt."),
        ("{0} apps, {1} shortcuts — {2} error(s), see the log.",
            "{0} ứng dụng, {1} phím tắt — {2} lỗi, xem nhật ký."),
        ("KeyPeek library has errors", "Thư viện KeyPeek có lỗi"),
        ("{0} problem(s) in your shortcut files — right-click → Open log for details.",
            "{0} lỗi trong file phím tắt của bạn — bấm chuột phải → Mở nhật ký để xem chi tiết."),
        ("\"{0}\" is running as administrator — Windows doesn't allow sending keys to it from a normal app. Press the shortcut on the keyboard instead.",
            "\"{0}\" đang chạy với quyền quản trị — Windows không cho ứng dụng thường gửi phím vào nó. Hãy bấm phím tắt trực tiếp trên bàn phím."),

        // ---- welcome ----
        ("Welcome to KeyPeek", "Chào mừng đến với KeyPeek"),
        ("KeyPeek is running", "KeyPeek đang chạy"),
        ("It sits in the tray and stays out of your way until you ask for it.",
            "Nó nằm ở khay hệ thống và không làm phiền cho tới khi bạn cần."),
        ("Hold it for half a second", "Giữ phím khoảng nửa giây"),
        ("The shortcuts of whatever app you're in appear. Let go and they're gone.",
            "Phím tắt của ứng dụng bạn đang dùng sẽ hiện ra. Thả tay là biến mất."),
        ("Click a row to run it", "Bấm một hàng để chạy phím tắt đó"),
        ("KeyPeek presses the shortcut for you in the app underneath.",
            "KeyPeek bấm phím tắt đó giúp bạn trong ứng dụng bên dưới."),
        ("Everything else lives in the tray icon", "Mọi thứ khác nằm ở biểu tượng khay"),
        ("Double-click it for settings, the shortcut library and conflicts.",
            "Bấm đúp vào nó để mở cài đặt, thư viện phím tắt và xung đột."),
        ("KeyPeek watches for held modifier keys only. It never records what you type, and nothing leaves this machine.",
            "KeyPeek chỉ quan sát các phím bổ trợ được giữ. Nó không bao giờ ghi lại thứ bạn gõ, và không gì rời khỏi máy này."),
        ("Open settings", "Mở cài đặt"),
        ("Got it", "Đã hiểu"),

        // ---- edit-shortcuts dialog ----
        ("My shortcuts for {0}", "Phím tắt của tôi cho {0}"),
        ("Your edits live in your own file. Library updates never overwrite them, and a shortcut you add here wins over the shipped one.",
            "Chỉnh sửa của bạn nằm trong file riêng của bạn. Cập nhật thư viện không bao giờ ghi đè lên, và phím tắt bạn thêm ở đây thắng phím tắt có sẵn."),
        ("Add a shortcut", "Thêm phím tắt"),
        // Distinct from the card title above on purpose: the table is bidirectional, and
        // two English keys sharing one Vietnamese face cannot round-trip a language switch.
        ("Keys", "Phím"),
        ("Click here, then press the shortcut", "Bấm vào đây rồi nhấn tổ hợp phím"),
        ("Press the shortcut…", "Nhấn tổ hợp phím…"),
        ("Esc clears · press a second combination to record a sequence (max 3)",
            "Esc để xoá · nhấn tổ hợp thứ hai để ghi thành chuỗi (tối đa 3)"),
        ("What it does", "Tác dụng"),
        ("Group", "Nhóm"),
        ("Suggest it", "Đưa vào gợi ý"),
        ("Add shortcut", "Thêm vào danh sách"),
        ("Your shortcuts", "Phím tắt của bạn"),
        ("Your shortcuts ({0})", "Phím tắt của bạn ({0})"),
        ("Nothing yet. Anything you add appears here and in the panel.",
            "Chưa có gì. Những gì bạn thêm sẽ hiện ở đây và trong bảng."),
        ("Open the file", "Mở file này"),
        ("Done", "Xong"),
        ("“{0}” already does “{1}” in {2}. Adding it will replace that row.",
            "“{0}” đã có tác dụng “{1}” trong {2}. Thêm vào sẽ thay thế hàng đó."),
        ("Windows handles most Win+… shortcuts before an app sees them.",
            "Windows xử lý hầu hết tổ hợp Win+… trước khi ứng dụng kịp nhận."),
        ("Could not save: {0}", "Không lưu được: {0}"),
        ("then", "rồi"),
        ("A shortcut can be at most {0} steps — Esc to start over.",
            "Một phím tắt tối đa {0} bước — nhấn Esc để làm lại."),

        // ---- add-app dialog ----
        ("Create a shortcut definition for an app. The file opens in your editor afterwards.",
            "Tạo định nghĩa phím tắt cho một ứng dụng. Sau đó file sẽ mở trong trình soạn thảo của bạn."),
        ("Display name", "Tên hiển thị"),
        ("Process name (no .exe)", "Tên tiến trình (không kèm .exe)"),
        ("Running apps", "Ứng dụng đang chạy"),
        ("Cancel", "Huỷ"),
        ("Pick a running app", "Chọn ứng dụng đang chạy"),
        ("Choose an app to exclude. The overlay never appears while it is focused.",
            "Chọn một ứng dụng để loại trừ. Bảng sẽ không bao giờ hiện khi nó đang được chọn."),
        ("Select", "Chọn"),
    };

    private static readonly Dictionary<string, (string En, string Vi)> Table = BuildTable();

    private static Dictionary<string, (string En, string Vi)> BuildTable()
    {
        // Both directions in one map, so TryLocalize can take text in either language.
        // Add() (not indexer) on the English side: a duplicated key is a table bug and
        // must fail loudly in the tests, not silently keep the last translation.
        var table = new Dictionary<string, (string, string)>(Pairs.Length * 2, StringComparer.Ordinal);
        foreach ((string en, string vi) in Pairs)
        {
            table.Add(en, (en, vi));
            table.TryAdd(vi, (en, vi)); // a Vietnamese word may serve two English keys
        }
        return table;
    }
}
