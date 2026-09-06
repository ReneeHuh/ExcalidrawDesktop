export const supportedLanguageCodes = [
  "en",
  "es-ES",
  "fr-FR",
  "de-DE",
  "pt-BR",
  "ja-JP",
  "zh-CN",
  "ar-SA",
] as const;

export type SupportedLanguageCode = (typeof supportedLanguageCodes)[number];

export type DesktopStringKey =
  | "editorLabel"
  | "newTab"
  | "open"
  | "save"
  | "saveAs"
  | "openFailed"
  | "saveFailed"
  | "exportRunning"
  | "exportFailed"
  | "bridgeUnavailable"
  | "bridgeTimeout"
  | "documentNotFound"
  | "documentAccessDenied"
  | "documentReadFailed"
  | "documentTooLarge"
  | "documentPathUnavailable"
  | "documentChangedExternally"
  | "documentAlreadyOpen"
  | "documentWriteFailed"
  | "documentInvalid"
  | "exportEmpty" | "exportDimensions" | "exportBytes" | "exportInvalid" | "exportUpload"
  | "libraryUnavailable" | "recoveryUnavailable" | "settingsRetry";

type SpecificStringKey = "exportEmpty" | "exportDimensions" | "exportBytes" | "exportInvalid" | "exportUpload" | "libraryUnavailable" | "recoveryUnavailable" | "settingsRetry";
type BaseCatalog = Record<Exclude<DesktopStringKey, SpecificStringKey>, string>;

const en: Record<DesktopStringKey, string> = {
  editorLabel: "Excalidraw Desktop editor",
  newTab: "New Tab",
  open: "Open…",
  save: "Save",
  saveAs: "Save As…",
  openFailed: "The drawing could not be opened.",
  saveFailed: "The drawing could not be saved.",
  exportRunning: "An image export is already running for this drawing.",
  exportFailed: "The drawing could not be exported as a PNG.",
  bridgeUnavailable: "The desktop bridge is unavailable.",
  bridgeTimeout: "The desktop operation timed out.",
  documentNotFound: "The drawing no longer exists at that location.",
  documentAccessDenied: "Excalidraw Desktop does not have permission to access that drawing.",
  documentReadFailed: "The drawing could not be read.",
  documentTooLarge: "The selected drawing exceeds the 50 MB desktop document limit.",
  documentPathUnavailable: "The selected drawing does not have a local file path.",
  documentChangedExternally: "The drawing changed outside Excalidraw Desktop. Resolve the conflict before saving.",
  documentAlreadyOpen: "That drawing is already open in another tab. Choose a different file name.",
  documentWriteFailed: "The drawing could not be written to disk.",
  documentInvalid: "The selected file is not a valid Excalidraw drawing.",
  exportEmpty: "Add something to the drawing before exporting.",
  exportDimensions: "Image dimensions exceed the limit; reduce the drawing size.",
  exportBytes: "The PNG exceeds 100 MB.",
  exportInvalid: "The editor returned an invalid PNG. Try again.",
  exportUpload: "Could not write the PNG to the selected file. Check access and disk space, then retry.",
  libraryUnavailable: "Library changes could not be saved. Retry before closing or export your library.",
  recoveryUnavailable: "Recovery is unavailable. Save your drawing to protect recent changes.",
  settingsRetry: "Retry",
};

const catalogs: Record<SupportedLanguageCode, BaseCatalog> = {
  en,
  "es-ES": {
    editorLabel: "Editor de Excalidraw Desktop",
    newTab: "Nueva pestaña",
    open: "Abrir…",
    save: "Guardar",
    saveAs: "Guardar como…",
    openFailed: "No se pudo abrir el dibujo.",
    saveFailed: "No se pudo guardar el dibujo.",
    exportRunning: "Ya hay una exportación de imagen en curso para este dibujo.",
    exportFailed: "No se pudo exportar el dibujo como PNG.",
    bridgeUnavailable: "El puente de escritorio no está disponible.",
    bridgeTimeout: "La operación de escritorio agotó el tiempo de espera.",
    documentNotFound: "El dibujo ya no existe en esa ubicación.",
    documentAccessDenied: "Excalidraw Desktop no tiene permiso para acceder a ese dibujo.",
    documentReadFailed: "No se pudo leer el dibujo.",
    documentTooLarge: "El dibujo seleccionado supera el límite de 50 MB.",
    documentPathUnavailable: "El dibujo seleccionado no tiene una ruta de archivo local.",
    documentChangedExternally: "El dibujo cambió fuera de Excalidraw Desktop. Resuelve el conflicto antes de guardarlo.",
    documentAlreadyOpen: "Ese dibujo ya está abierto en otra pestaña. Elige otro nombre de archivo.",
    documentWriteFailed: "No se pudo escribir el dibujo en el disco.",
    documentInvalid: "El archivo seleccionado no es un dibujo de Excalidraw válido.",
  },
  "fr-FR": {
    editorLabel: "Éditeur Excalidraw Desktop",
    newTab: "Nouvel onglet",
    open: "Ouvrir…",
    save: "Enregistrer",
    saveAs: "Enregistrer sous…",
    openFailed: "Impossible d’ouvrir le dessin.",
    saveFailed: "Impossible d’enregistrer le dessin.",
    exportRunning: "Une exportation d’image est déjà en cours pour ce dessin.",
    exportFailed: "Impossible d’exporter le dessin au format PNG.",
    bridgeUnavailable: "Le pont avec l’application de bureau est indisponible.",
    bridgeTimeout: "L’opération de bureau a expiré.",
    documentNotFound: "Le dessin n’existe plus à cet emplacement.",
    documentAccessDenied: "Excalidraw Desktop n’est pas autorisé à accéder à ce dessin.",
    documentReadFailed: "Impossible de lire le dessin.",
    documentTooLarge: "Le dessin sélectionné dépasse la limite de 50 Mo.",
    documentPathUnavailable: "Le dessin sélectionné ne possède pas de chemin local.",
    documentChangedExternally: "Le dessin a été modifié hors d’Excalidraw Desktop. Résolvez le conflit avant de l’enregistrer.",
    documentAlreadyOpen: "Ce dessin est déjà ouvert dans un autre onglet. Choisissez un autre nom de fichier.",
    documentWriteFailed: "Impossible d’écrire le dessin sur le disque.",
    documentInvalid: "Le fichier sélectionné n’est pas un dessin Excalidraw valide.",
  },
  "de-DE": {
    editorLabel: "Excalidraw-Desktop-Editor",
    newTab: "Neuer Tab",
    open: "Öffnen…",
    save: "Speichern",
    saveAs: "Speichern unter…",
    openFailed: "Die Zeichnung konnte nicht geöffnet werden.",
    saveFailed: "Die Zeichnung konnte nicht gespeichert werden.",
    exportRunning: "Für diese Zeichnung wird bereits ein Bild exportiert.",
    exportFailed: "Die Zeichnung konnte nicht als PNG exportiert werden.",
    bridgeUnavailable: "Die Desktop-Verbindung ist nicht verfügbar.",
    bridgeTimeout: "Zeitüberschreitung beim Desktopvorgang.",
    documentNotFound: "Die Zeichnung ist an diesem Speicherort nicht mehr vorhanden.",
    documentAccessDenied: "Excalidraw Desktop darf nicht auf diese Zeichnung zugreifen.",
    documentReadFailed: "Die Zeichnung konnte nicht gelesen werden.",
    documentTooLarge: "Die ausgewählte Zeichnung überschreitet das Limit von 50 MB.",
    documentPathUnavailable: "Die ausgewählte Zeichnung hat keinen lokalen Dateipfad.",
    documentChangedExternally: "Die Zeichnung wurde außerhalb von Excalidraw Desktop geändert. Lösen Sie den Konflikt vor dem Speichern.",
    documentAlreadyOpen: "Diese Zeichnung ist bereits in einem anderen Tab geöffnet. Wählen Sie einen anderen Dateinamen.",
    documentWriteFailed: "Die Zeichnung konnte nicht auf den Datenträger geschrieben werden.",
    documentInvalid: "Die ausgewählte Datei ist keine gültige Excalidraw-Zeichnung.",
  },
  "pt-BR": {
    editorLabel: "Editor do Excalidraw Desktop",
    newTab: "Nova guia",
    open: "Abrir…",
    save: "Salvar",
    saveAs: "Salvar como…",
    openFailed: "Não foi possível abrir o desenho.",
    saveFailed: "Não foi possível salvar o desenho.",
    exportRunning: "Já há uma exportação de imagem em andamento para este desenho.",
    exportFailed: "Não foi possível exportar o desenho como PNG.",
    bridgeUnavailable: "A ponte com o aplicativo não está disponível.",
    bridgeTimeout: "A operação do aplicativo expirou.",
    documentNotFound: "O desenho não existe mais nesse local.",
    documentAccessDenied: "O Excalidraw Desktop não tem permissão para acessar esse desenho.",
    documentReadFailed: "Não foi possível ler o desenho.",
    documentTooLarge: "O desenho selecionado excede o limite de 50 MB.",
    documentPathUnavailable: "O desenho selecionado não tem um caminho de arquivo local.",
    documentChangedExternally: "O desenho foi alterado fora do Excalidraw Desktop. Resolva o conflito antes de salvar.",
    documentAlreadyOpen: "Esse desenho já está aberto em outra guia. Escolha outro nome de arquivo.",
    documentWriteFailed: "Não foi possível gravar o desenho no disco.",
    documentInvalid: "O arquivo selecionado não é um desenho válido do Excalidraw.",
  },
  "ja-JP": {
    editorLabel: "Excalidraw Desktop エディター",
    newTab: "新しいタブ",
    open: "開く…",
    save: "保存",
    saveAs: "名前を付けて保存…",
    openFailed: "図面を開けませんでした。",
    saveFailed: "図面を保存できませんでした。",
    exportRunning: "この図面の画像エクスポートは既に実行中です。",
    exportFailed: "図面を PNG としてエクスポートできませんでした。",
    bridgeUnavailable: "デスクトップブリッジを利用できません。",
    bridgeTimeout: "デスクトップ操作がタイムアウトしました。",
    documentNotFound: "その場所に図面が存在しません。",
    documentAccessDenied: "Excalidraw Desktop にはこの図面にアクセスする権限がありません。",
    documentReadFailed: "図面を読み取れませんでした。",
    documentTooLarge: "選択した図面は 50 MB の制限を超えています。",
    documentPathUnavailable: "選択した図面にはローカルファイルパスがありません。",
    documentChangedExternally: "図面が Excalidraw Desktop の外部で変更されました。保存前に競合を解決してください。",
    documentAlreadyOpen: "その図面は別のタブで既に開かれています。別のファイル名を選択してください。",
    documentWriteFailed: "図面をディスクに書き込めませんでした。",
    documentInvalid: "選択したファイルは有効な Excalidraw 図面ではありません。",
  },
  "zh-CN": {
    editorLabel: "Excalidraw Desktop 编辑器",
    newTab: "新建标签页",
    open: "打开…",
    save: "保存",
    saveAs: "另存为…",
    openFailed: "无法打开绘图。",
    saveFailed: "无法保存绘图。",
    exportRunning: "此绘图的图像导出已在进行中。",
    exportFailed: "无法将绘图导出为 PNG。",
    bridgeUnavailable: "桌面桥接不可用。",
    bridgeTimeout: "桌面操作超时。",
    documentNotFound: "该位置已不存在此绘图。",
    documentAccessDenied: "Excalidraw Desktop 无权访问此绘图。",
    documentReadFailed: "无法读取绘图。",
    documentTooLarge: "所选绘图超过 50 MB 限制。",
    documentPathUnavailable: "所选绘图没有本地文件路径。",
    documentChangedExternally: "绘图已在 Excalidraw Desktop 外部更改。请在保存前解决冲突。",
    documentAlreadyOpen: "该绘图已在另一个标签页中打开。请选择其他文件名。",
    documentWriteFailed: "无法将绘图写入磁盘。",
    documentInvalid: "所选文件不是有效的 Excalidraw 绘图。",
  },
  "ar-SA": {
    editorLabel: "محرر Excalidraw Desktop",
    newTab: "علامة تبويب جديدة",
    open: "فتح…",
    save: "حفظ",
    saveAs: "حفظ باسم…",
    openFailed: "تعذر فتح الرسم.",
    saveFailed: "تعذر حفظ الرسم.",
    exportRunning: "يجري بالفعل تصدير صورة لهذا الرسم.",
    exportFailed: "تعذر تصدير الرسم بصيغة PNG.",
    bridgeUnavailable: "جسر تطبيق سطح المكتب غير متاح.",
    bridgeTimeout: "انتهت مهلة عملية سطح المكتب.",
    documentNotFound: "لم يعد الرسم موجودًا في ذلك الموقع.",
    documentAccessDenied: "لا يملك Excalidraw Desktop إذنًا للوصول إلى هذا الرسم.",
    documentReadFailed: "تعذرت قراءة الرسم.",
    documentTooLarge: "يتجاوز الرسم المحدد حد 50 ميغابايت.",
    documentPathUnavailable: "لا يحتوي الرسم المحدد على مسار ملف محلي.",
    documentChangedExternally: "تم تغيير الرسم خارج Excalidraw Desktop. قم بحل التعارض قبل الحفظ.",
    documentAlreadyOpen: "هذا الرسم مفتوح بالفعل في علامة تبويب أخرى. اختر اسم ملف مختلفًا.",
    documentWriteFailed: "تعذرت كتابة الرسم على القرص.",
    documentInvalid: "الملف المحدد ليس رسم Excalidraw صالحًا.",
  },
};

const specific: Record<Exclude<SupportedLanguageCode, "en">, Record<SpecificStringKey, string>> = {
  "es-ES": { exportEmpty: "Añade algo al dibujo antes de exportar.", exportDimensions: "Las dimensiones superan el límite; reduce el tamaño del dibujo.", exportBytes: "El PNG supera los 100 MB.", exportInvalid: "El editor devolvió un PNG no válido. Inténtalo de nuevo.", exportUpload: "No se pudo escribir el PNG en el archivo elegido. Comprueba el acceso y el disco e inténtalo de nuevo.", libraryUnavailable: "No se pudieron guardar los cambios de la biblioteca. Reintenta antes de cerrar o exporta tu biblioteca.", recoveryUnavailable: "La recuperación no está disponible. Guarda el dibujo para proteger los cambios recientes.", settingsRetry: "Reintentar" },
  "fr-FR": { exportEmpty: "Ajoutez quelque chose au dessin avant l’exportation.", exportDimensions: "Les dimensions dépassent la limite ; réduisez la taille du dessin.", exportBytes: "Le PNG dépasse 100 Mo.", exportInvalid: "L’éditeur a renvoyé un PNG invalide. Réessayez.", exportUpload: "Impossible d’écrire le PNG dans le fichier choisi. Vérifiez l’accès et l’espace disque, puis réessayez.", libraryUnavailable: "Les modifications de la bibliothèque n’ont pas pu être enregistrées. Réessayez avant de fermer ou exportez votre bibliothèque.", recoveryUnavailable: "La récupération est indisponible. Enregistrez le dessin pour protéger les modifications récentes.", settingsRetry: "Retry" },
  "de-DE": { exportEmpty: "Fügen Sie vor dem Export etwas zur Zeichnung hinzu.", exportDimensions: "Die Bildabmessungen überschreiten das Limit; verkleinern Sie die Zeichnung.", exportBytes: "Die PNG-Datei überschreitet 100 MB.", exportInvalid: "Der Editor gab ein ungültiges PNG zurück. Versuchen Sie es erneut.", exportUpload: "Das PNG konnte nicht in die ausgewählte Datei geschrieben werden. Prüfen Sie Zugriff und Speicherplatz und versuchen Sie es erneut.", libraryUnavailable: "Bibliotheksänderungen konnten nicht gespeichert werden. Versuchen Sie es vor dem Schließen erneut oder exportieren Sie die Bibliothek.", recoveryUnavailable: "Wiederherstellung ist nicht verfügbar. Speichern Sie die Zeichnung, um aktuelle Änderungen zu schützen.", settingsRetry: "Retry" },
  "pt-BR": { exportEmpty: "Adicione algo ao desenho antes de exportar.", exportDimensions: "As dimensões excedem o limite; reduza o tamanho do desenho.", exportBytes: "O PNG excede 100 MB.", exportInvalid: "O editor retornou um PNG inválido. Tente novamente.", exportUpload: "Não foi possível gravar o PNG no arquivo escolhido. Verifique o acesso e o disco e tente novamente.", libraryUnavailable: "Não foi possível salvar as alterações da biblioteca. Tente novamente antes de fechar ou exporte sua biblioteca.", recoveryUnavailable: "A recuperação está indisponível. Salve o desenho para proteger as alterações recentes.", settingsRetry: "Retry" },
  "ja-JP": { exportEmpty: "エクスポートする前に図面に何か追加してください。", exportDimensions: "画像の寸法が上限を超えています。図面を小さくしてください。", exportBytes: "PNG が 100 MB を超えています。", exportInvalid: "エディターが無効な PNG を返しました。再試行してください。", exportUpload: "選択したファイルに PNG を書き込めませんでした。アクセス権とディスクを確認して再試行してください。", libraryUnavailable: "ライブラリの変更を保存できませんでした。閉じる前に再試行するか、ライブラリをエクスポートしてください。", recoveryUnavailable: "復元を利用できません。最近の変更を保護するため図面を保存してください。", settingsRetry: "Retry" },
  "zh-CN": { exportEmpty: "导出前请先向绘图添加内容。", exportDimensions: "图像尺寸超出限制，请缩小绘图。", exportBytes: "PNG 超过 100 MB。", exportInvalid: "编辑器返回了无效 PNG，请重试。", exportUpload: "无法将 PNG 写入所选文件。请检查访问权限和磁盘空间后重试。", libraryUnavailable: "无法保存图库更改。请在关闭前重试，或导出图库。", recoveryUnavailable: "恢复不可用。请保存绘图以保护最近的更改。", settingsRetry: "Retry" },
  "ar-SA": { exportEmpty: "أضف شيئًا إلى الرسم قبل التصدير.", exportDimensions: "تتجاوز أبعاد الصورة الحد المسموح؛ صغّر الرسم.", exportBytes: "يتجاوز PNG حجم 100 ميغابايت.", exportInvalid: "أعاد المحرر ملف PNG غير صالح. حاول مرة أخرى.", exportUpload: "تعذرت كتابة PNG في الملف المحدد. تحقق من الوصول ومساحة القرص ثم حاول مرة أخرى.", libraryUnavailable: "تعذر حفظ تغييرات المكتبة. حاول مرة أخرى قبل الإغلاق أو صدّر مكتبتك.", recoveryUnavailable: "الاسترداد غير متاح. احفظ الرسم لحماية التغييرات الأخيرة.", settingsRetry: "Retry" },
};

export const isSupportedLanguageCode = (
  value: string,
): value is SupportedLanguageCode =>
  supportedLanguageCodes.includes(value as SupportedLanguageCode);

export const getDesktopString = (
  langCode: string,
  key: DesktopStringKey,
) => {
  const code = isSupportedLanguageCode(langCode) ? langCode : "en";
  if (code !== "en" && key in specific[code])
    return specific[code][key as SpecificStringKey];
  return code === "en" ? en[key] : catalogs[code][key as keyof BaseCatalog] ?? en[key];
};

const errorKeys: Record<string, DesktopStringKey> = {
  ExportEmpty: "exportEmpty",
  ExportDimensionLimit: "exportDimensions",
  ExportTooLarge: "exportDimensions",
  ExportByteLimit: "exportBytes",
  ExportInvalidImage: "exportInvalid",
  ExportUploadFailed: "exportUpload",
  BridgeUnavailable: "bridgeUnavailable",
  BridgeTimeout: "bridgeTimeout",
  DocumentNotFound: "documentNotFound",
  DocumentAccessDenied: "documentAccessDenied",
  DocumentReadFailed: "documentReadFailed",
  DocumentTooLarge: "documentTooLarge",
  DocumentPathUnavailable: "documentPathUnavailable",
  DocumentChangedExternally: "documentChangedExternally",
  DocumentAlreadyOpen: "documentAlreadyOpen",
  DocumentWriteFailed: "documentWriteFailed",
  DocumentInvalid: "documentInvalid",
};

export const getDesktopErrorString = (
  langCode: string,
  code: string | undefined,
  fallbackKey: "openFailed" | "saveFailed" | "exportFailed",
) => getDesktopString(langCode, (code && errorKeys[code]) || fallbackKey);
