using System.Collections.Generic;

namespace CustomSlimeCreator.Core
{
    public static class Loc
    {
        private static string _lang;

        public static string Lang
        {
            get
            {
                if (_lang != null) return _lang;
                _lang = "en";
                try
                {
                    var loc = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
                    if (loc != null)
                    {
                        var code = loc.Identifier.Code;
                        if (!string.IsNullOrEmpty(code)) _lang = (code.Length >= 2 ? code.Substring(0, 2) : code).ToLower();
                    }
                }
                catch { }
                return _lang;
            }
        }

        public static void Reset() => _lang = null;

        public static string T(string key)
        {
            if (M.TryGetValue(key, out var d))
            {
                if (d.TryGetValue(Lang, out var v)) return v;
                if (d.TryGetValue("en", out var e)) return e;
            }
            return key;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> M = new Dictionary<string, Dictionary<string, string>>
        {
            ["title"] = L("Custom Slime Creator", "Creador de Slimes", "Créateur de Slimes", "Custom Slime Creator", "Creatore di Slime", "Создание слаймов", "自定义史莱姆"),
            ["close"] = L("(F2 to close)", "(F2 para cerrar)", "(F2 pour fermer)", "(F2 schließen)", "(F2 per chiudere)", "(F2 для закрытия)", "(F2关闭)"),
            ["tab_look"] = L("Look", "Apariencia", "Apparence", "Aussehen", "Aspetto", "Внешность", "外观"),
            ["tab_parts"] = L("Parts", "Piezas", "Parties", "Teile", "Parti", "Части", "部件"),
            ["tab_options"] = L("Options", "Opciones", "Options", "Optionen", "Opzioni", "Настройки", "选项"),
            ["tab_saved"] = L("Saved", "Guardados", "Sauv.", "Gespeichert", "Salvati", "Сохранённые", "已保存"),
            ["tab_fusions"] = L("Fusions", "Fusiones", "Fusions", "Fusionen", "Fusioni", "Слияния", "融合"),
            ["fusions_title"] = L("Discovered fusions", "Fusiones descubiertas", "Fusions découvertes", "Entdeckte Fusionen", "Fusioni scoperte", "Открытые слияния", "已发现的融合"),
            ["fusions_none"] = L("No fusions discovered yet.", "Aún no descubriste fusiones.", "Aucune fusion découverte.", "Noch keine Fusionen entdeckt.", "Nessuna fusione scoperta.", "Слияния ещё не открыты.", "尚未发现融合。"),
            ["fusions_hint"] = L("Feed a custom slime another slime's plort to fuse them.", "Dale a un slime custom el plort de otro slime para fusionarlos.", "Donne à un slime custom le plort d'un autre slime pour les fusionner.", "Füttere einen Custom-Slime mit dem Plort eines anderen Slimes, um sie zu fusionieren.", "Dai a uno slime custom il plort di un altro slime per fonderli.", "Дайте кастомному слайму плорт другого слайма, чтобы объединить их.", "给自定义史莱姆喂食另一种史莱姆的普洛特即可融合。"),
            ["identity"] = L("Identity", "Identidad", "Identité", "Identität", "Identità", "Идентичность", "身份"),
            ["name"] = L("Name (A-Z):", "Nombre (A-Z):", "Nom (A-Z):", "Name (A-Z):", "Nome (A-Z):", "Имя (A-Z):", "名称(A-Z):"),
            ["display"] = L("Display:", "Nombre visible:", "Affiché:", "Anzeigename:", "Nome visualizzato:", "Отображаемое имя:", "显示名称:"),
            ["preset"] = L("Base preset:", "Slime base:", "Slime de base:", "Basis-Slime:", "Slime base:", "Базовый слайм:", "基础预设:"),
            ["colors"] = L("Colors", "Colores", "Couleurs", "Farben", "Colori", "Цвета", "颜色"),
            ["preview"] = L("Preview  ·  Icon", "Vista previa  ·  Icono", "Aperçu  ·  Icône", "Vorschau  ·  Symbol", "Anteprima  ·  Icona", "Предпросмотр  ·  Иконка", "预览·图标"),
            ["preview_hint"] = L("Live model (left) — in-game icon (right). Arrows center the shot.",
                "Modelo (izq.) — icono (der.). Las flechas centran la foto.",
                "Modèle (g.) — icône (d.). Les flèches centrent.",
                "Live-Modell (l.) — Spielsymbol (r.). Pfeile zentrieren.",
                "Modello (sx) — icona (dx). Le frecce centrano.",
                "Модель (слева) — иконка (справа). Стрелки центрируют.",
                "实时模型(左)—游戏图标(右)。箭头居中。"),
            ["shader_fx"] = L("Shader Effects", "Efectos de shader", "Effets de shader", "Shader-Effekte", "Effetti shader", "Эффекты шейдера", "着色器效果"),
            ["body_fx"] = L("Body Effects (mix looks of other slimes)", "Efectos de cuerpo (mezcla otros slimes)", "Effets de corps", "Körper-Effekte", "Effetti corpo", "Эффекты тела (смесь других слаймов)", "身体效果(混合其他史莱姆)"),
            ["options"] = L("Options", "Opciones", "Options", "Optionen", "Opzioni", "Настройки", "选项"),
            ["diet"] = L("Diet", "Dieta", "Régime", "Nahrung", "Dieta", "Рацион", "饮食"),
            ["zones"] = L("Spawn Zones (empty = editor only)", "Zonas de aparición (vacío = solo editor)", "Zones d'apparition", "Spawn-Zonen", "Zone di spawn", "Зоны появления (пусто = только редактор)", "生成区域(空=仅编辑器)"),
            ["parts_title"] = L("Parts from other slimes", "Piezas de otros slimes", "Parties d'autres slimes", "Teile anderer Slimes", "Parti da altri slime", "Части от других слаймов", "来自其他史莱姆的部件"),
            ["part"] = L("Part:", "Pieza:", "Partie:", "Teil:", "Parte:", "Часть:", "部件:"),
            ["from"] = L("From:", "De:", "De:", "Von:", "Da:", "Из:", "来自:"),
            ["recolor"] = L(" Recolor", " Recolorear", " Recolorer", " Umfärben", " Ricolora", " Перекрасить", "重新着色"),
            ["add_part"] = L("+ Add part", "+ Agregar pieza", "+ Ajouter", "+ Teil hinzufügen", "+ Aggiungi parte", "+ Добавить часть", "+添加部件"),
            ["remove"] = L("Remove", "Quitar", "Retirer", "Entfernen", "Rimuovi", "Удалить", "移除"),
            ["load"] = L("Load", "Cargar", "Charger", "Laden", "Carica", "Загрузить", "加载"),
            ["delete"] = L("Delete", "Borrar", "Suppr.", "Löschen", "Elimina", "Удалить", "删除"),
            ["new"] = L("New", "Nuevo", "Nouveau", "Neu", "Nuovo", "Новый", "新建"),
            ["create"] = L("Create / Update", "Crear / Actualizar", "Créer / MàJ", "Erstellen / Aktualis.", "Crea / Aggiorna", "Создать / Обновить", "创建/更新"),
            ["spawn"] = L("Spawn", "Aparecer", "Apparaître", "Erscheinen", "Genera", "Появиться", "生成"),
            ["save"] = L("Save", "Guardar", "Sauver", "Speichern", "Salva", "Сохранить", "保存"),
            ["tut_skip"] = L("Skip tutorial", "Saltar tutorial", "Passer", "Tutorial überspringen", "Salta tutorial", "Пропустить обучение", "跳过教程"),
            ["tut_next"] = L("Continue", "Continuar", "Continuer", "Weiter", "Continua", "Продолжить", "继续"),
            ["tut_title"] = L("How to use", "Cómo usar", "Comment utiliser", "Wie benutzen", "Come usare", "Как использовать", "如何使用"),

            // body effects, options, plort labels
            ["twin_swirl"] = L(" Twin swirl", " Remolino gemelo", " Tourbillon jumeau", " Zwillingswirbel", " Mulinello gemello", " Двойной водоворот", "双旋涡"),
            ["sloomber_stars"] = L(" Sloomber stars", " Estrellas Sloomber", " Étoiles Sloomber", " Sloomber-Sterne", " Stelle Sloomber", " Звёзды Слумбера", "昏睡之星"),
            ["aura_hyper"] = L(" Aura (Hyper)", " Aura (Hyper)", " Aura (Hyper)", " Aura (Hyper)", " Aura (Hyper)", " Аура (Hyper)", "光环(Hyper)"),
            ["crystal_shards"] = L(" Crystal shards", " Fragmentos de cristal", " Éclats de cristal", " Kristallsplitter", " Frammenti di cristallo", " Кристальные осколки", "水晶碎片"),
            ["rock_plating"] = L(" Rock plating", " Placas de roca", " Plaques rocheuses", " Felsplatten", " Placcatura roccia", " Каменные плиты", "岩石板甲"),
            ["angler_lure"] = L(" Angler lure", " Señuelo de Angler", " Leurre d'Angler", " Angler-Köder", " Esca Angler", " Приманка Англера", "灯笼鱼诱饵"),
            ["hunter_parts"] = L(" Hunter parts", " Partes de Hunter", " Parties de Hunter", " Hunter-Teile", " Parti Hunter", " Части Хантера", "猎人部件"),
            ["ringtail_parts"] = L(" Ringtail parts", " Partes de Ringtail", " Parties de Ringtail", " Ringtail-Teile", " Parti Ringtail", " Части Рингтейла", "环尾部件"),
            ["can_largofy"] = L(" Can largofy", " Puede largoficar", " Peut largofier", " Kann Largo werden", " Può diventare largo", " Может быть ларго", "可融合"),
            ["all_largos"] = L(" All largos", " Todos los largos", " Tous les largos", " Alle Largos", " Tutti i larghi", " Все ларго", "所有融合"),
            ["edible_tarr"] = L(" Edible by Tarrs", " Comestible por Tarrs", " Comestible par Tarrs", " Essbar für Tarrs", " Commestibile dai Tarr", " Съедобно для Тарров", "可被焦油怪吃掉"),
            ["vaccable"] = L(" Vaccable", " Vaciable", " Vaccable", " Einsaugbar", " Aspirabile", " Ваккумируемый", "可吸入"),
            ["radiant"] = L(" Radiant", " Radiante", " Radieux", " Strahlend", " Radiante", " Сияющий", "发光"),
            ["plort"] = L("Plort", "Plort", "Plort", "Plort", "Plort", "Плорты", "普洛特"),
            ["has_plort"] = L(" Has plort", " Tiene plort", " A un plort", " Hat Plort", " Ha il plort", " Есть плорт", "有普洛特"),
            ["value"] = L("Value:", "Valor:", "Valeur:", "Wert:", "Valore:", "Цена:", "价值:"),
            ["plort_top"] = L("Plort Top:", "Plort Arriba:", "Plort Haut:", "Plort Oben:", "Plort Sup.:", "Плорт верх:", "普洛特顶色:"),
            ["plort_mid"] = L("Plort Mid:", "Plort Medio:", "Plort Mil.:", "Plort Mitte:", "Plort Med.:", "Плорт середина:", "普洛特中色:"),
            ["plort_bot"] = L("Plort Bot:", "Plort Abajo:", "Plort Bas:", "Plort Unten:", "Plort Inf.:", "Плорт низ:", "普洛特底色:"),
            ["notes"] = L("Notes", "Notas", "Notes", "Notizen", "Note", "Заметки", "备注"),
            ["notes_text"] = L("Changes apply live via 'Create / Update'.", "Los cambios se aplican con 'Crear / Actualizar'.", "Les changements s'appliquent avec 'Créer / MàJ'.", "Änderungen live via 'Erstellen / Aktualis.' anwenden.", "Le modifiche si applicano con 'Crea / Aggiorna'.", "Изменения применяются через 'Создать / Обновить'.", "通过「创建/更新」实时应用更改。"),
            ["saved_slimes"] = L("Saved slimes", "Slimes guardados", "Slimes sauvés", "Gespeicherte Slimes", "Slime salvati", "Сохранённые слаймы", "已保存的史莱姆"),
            ["folder"] = L("Folder", "Carpeta", "Dossier", "Ordner", "Cartella", "Папка", "文件夹"),
            ["open_folder"] = L("Open configs folder", "Abrir carpeta de configs", "Ouvrir le dossier", "Konfig-Ordner öffnen", "Apri cartella config", "Открыть папку конфигов", "打开配置文件夹"),
            ["no_saved"] = L("No saved slimes yet.", "Aún no hay slimes guardados.", "Aucun slime sauvegardé.", "Noch keine gespeicherten Slimes.", "Nessuno slime salvato.", "Нет сохранённых слаймов.", "还没有已保存的史莱姆。"),

            // Color field labels in Look tab
            ["clr_top"] = L("Top", "Arriba", "Haut", "Oben", "Sup.", "Верх", "顶部"),
            ["clr_mid"] = L("Middle", "Medio", "Milieu", "Mitte", "Med.", "Середина", "中部"),
            ["clr_bot"] = L("Bottom", "Abajo", "Bas", "Unten", "Inf.", "Низ", "底部"),
            ["clr_vac"] = L("Vac", "Vacío", "Vide", "Vak.", "Vuoto", "Вакуум", "吸入"),
        };

        public static string[] Tutorial()
        {
            if (Lang == "es") return new[]
            {
                "Bienvenido al Creador de Slimes.\n\nEn la pestaña 'Apariencia' elegís un slime BASE, cambiás sus colores con las barras, y tocás 'Crear / Actualizar' para aplicar en vivo.",
                "Pestaña 'Piezas': agregale alas, orejas, cola, pinchos, etc. de OTROS slimes. Solo aparecen las piezas que ese slime realmente tiene. Podés recoloréarlas.",
                "Pestaña 'Opciones': 'Efectos de cuerpo' mezcla el look de otros slimes (aura, cristales...). También la dieta (qué come) y las zonas donde aparece.",
                "Botón 'Aparecer': crea uno frente a vos. 'Guardar': lo guarda en disco. La vista previa (arriba) muestra tu slime en vivo; las flechas centran el icono.",
            };
            return new[]
            {
                "Welcome to Custom Slime Creator.\n\nIn the 'Look' tab pick a BASE slime, change its colors with the sliders, and press 'Create / Update' to apply live.",
                "'Parts' tab: add wings, ears, tail, spikes, etc. from OTHER slimes. Only the parts that slime actually has show up. You can recolor them.",
                "'Options' tab: 'Body Effects' mix in the look of other slimes (aura, crystals...). Also the diet (what it eats) and the zones it spawns in.",
                "'Spawn' drops one in front of you. 'Save' writes it to disk. The preview (top) shows your slime live; the arrows center the icon.",
            };
        }

        private static Dictionary<string, string> L(string en, string es, string fr, string de, string it, string ru, string zh)
            => new Dictionary<string, string> { ["en"] = en, ["es"] = es, ["fr"] = fr, ["de"] = de, ["it"] = it, ["ru"] = ru, ["zh"] = zh };
    }
}
