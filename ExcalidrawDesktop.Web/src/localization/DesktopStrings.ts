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
  | "documentInvalid";

type DesktopCatalog = Record<DesktopStringKey, string>;

const en: DesktopCatalog = {
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
};

const catalogs: Record<SupportedLanguageCode, DesktopCatalog> = {
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

export const isSupportedLanguageCode = (
  value: string,
): value is SupportedLanguageCode =>
  supportedLanguageCodes.includes(value as SupportedLanguageCode);

export const getDesktopString = (
  langCode: string,
  key: DesktopStringKey,
) => (isSupportedLanguageCode(langCode) ? catalogs[langCode] : en)[key];

const errorKeys: Record<string, DesktopStringKey> = {
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
