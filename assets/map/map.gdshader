shader_type canvas_item;

// Карта мира. Одна текстура ID-карты + LUT-таблицы цветов.
// Кодирование ID: пиксель = (provinceIndex + 1) в RGB (0 = океан).
// color_lut: 1 пиксель на провинцию -> цвет страны / режима карты.
// country_lut: 1 пиксель на провинцию -> (countryId + 1) в RGB.
// Все текстуры сэмплируются filter_nearest — без «мыла» и мусорных id на границах.

uniform sampler2D id_map : filter_nearest;
uniform sampler2D color_lut : filter_nearest;
uniform sampler2D country_lut : filter_nearest;
uniform sampler2D border_mask : filter_nearest;

uniform float province_count = 1.0;
uniform float map_width = 2880.0;
uniform float map_height = 1440.0;

uniform int hover_id = -1;            // индекс провинции (0-based), -1 = нет
uniform int selected_id = -1;         // индекс провинции (0-based), -1 = нет
uniform int highlight_country = -1;   // id страны (0-based), -1 = нет

uniform vec4 ocean_color = vec4(0.08, 0.11, 0.18, 1.0);
uniform vec4 border_color = vec4(0.03, 0.04, 0.07, 1.0);
uniform vec4 hover_color = vec4(1.0, 1.0, 1.0, 0.55);
uniform vec4 selected_color = vec4(1.0, 0.80, 0.0, 0.70);
uniform vec4 highlight_overlay = vec4(1.0, 1.0, 1.0, 0.22);

int decode_id(vec4 c) {
    int r = int(c.r * 255.0 + 0.5);
    int g = int(c.g * 255.0 + 0.5);
    int b = int(c.b * 255.0 + 0.5);
    return r + g * 256 + b * 65536;
}

vec4 sample_lut(sampler2D lut, int index) {
    float u = (float(index) + 0.5) / province_count;
    return texture(lut, vec2(u, 0.5));
}

void fragment() {
    vec2 texel = vec2(1.0 / map_width, 1.0 / map_height);
    vec4 id_col = texture(id_map, UV);
    int id = decode_id(id_col);

    if (id <= 0) {
        COLOR = ocean_color;
    } else {
        int index = id - 1;
        COLOR = sample_lut(color_lut, index);

        if (highlight_country >= 0) {
            int cid = decode_id(sample_lut(country_lut, index)) - 1;
            if (cid == highlight_country) {
                COLOR.rgb = mix(COLOR.rgb, highlight_overlay.rgb, highlight_overlay.a);
            }
        }
    }

    // Границы (береговая линия + межпровинциальные) из border_mask.
    float b = texture(border_mask, UV).r;
    if (b > 0.5) {
        COLOR.rgb = mix(COLOR.rgb, border_color.rgb, 0.9);
    }

    // Обводка наведения/выбора — контур по различию id соседних пикселей.
    if (id > 0 && (hover_id >= 0 || selected_id >= 0)) {
        bool is_target = (id == hover_id + 1) || (id == selected_id + 1);
        if (is_target) {
            int l = decode_id(texture(id_map, UV - vec2(texel.x, 0.0)));
            int r = decode_id(texture(id_map, UV + vec2(texel.x, 0.0)));
            int t = decode_id(texture(id_map, UV - vec2(0.0, texel.y)));
            int d = decode_id(texture(id_map, UV + vec2(0.0, texel.y)));
            if (l != id || r != id || t != id || d != id) {
                vec4 outline = (id == selected_id + 1) ? selected_color : hover_color;
                COLOR.rgb = mix(COLOR.rgb, outline.rgb, outline.a);
            }
        }
    }
}
