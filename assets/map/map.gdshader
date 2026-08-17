shader_type canvas_item;

// Карта: одна текстура ID-карты (TEXTURE) + справочные текстуры.
// Кодирование: в ID-карте пиксель = (provinceIndex + 1) в RGB (0 = океан).
// color_lut: 1 пиксель на провинцию -> цвет страны (или режима карты).
// country_lut: 1 пиксель на провинцию -> (countryId + 1) в RGB.

uniform sampler2D color_lut : filter_nearest;
uniform sampler2D country_lut : filter_nearest;
uniform sampler2D border_mask : filter_nearest;

uniform float province_count = 1.0;
uniform vec2 texel_size = vec2(1.0 / 2880.0, 1.0 / 1440.0);
uniform int hover_id = -1;            // индекс провинции (0-based), -1 = нет
uniform int selected_id = -1;         // индекс провинции (0-based), -1 = нет
uniform int highlight_country = -1;   // id страны (0-based), -1 = нет

uniform vec4 ocean_color : source_color = vec4(0.09, 0.13, 0.20, 1.0);
uniform vec4 border_color : source_color = vec4(0.10, 0.10, 0.12, 1.0);
uniform vec4 hover_color : source_color = vec4(1.0, 1.0, 1.0, 0.85);
uniform vec4 selected_color : source_color = vec4(1.0, 0.84, 0.0, 0.9);
uniform vec4 highlight_overlay : source_color = vec4(1.0, 1.0, 1.0, 0.30);

int decode_id(vec4 c) {
	int r = int(c.r * 255.0 + 0.5);
	int g = int(c.g * 255.0 + 0.5);
	int b = int(c.b * 255.0 + 0.5);
	return r + g * 256 + b * 65536;
}

void fragment() {
	vec4 id_col = texture(TEXTURE, UV);
	int id = decode_id(id_col);

	if (id <= 0) {
		COLOR = ocean_color;
	} else {
		int index = id - 1;
		float u = (float(index) + 0.5) / province_count;
		COLOR = texture(color_lut, vec2(u, 0.5));

		// Подсветка страны (при выборе/наведении на страну).
		if (highlight_country >= 0) {
			vec4 cc = texture(country_lut, vec2(u, 0.5));
			int cid = decode_id(cc) - 1;
			if (cid == highlight_country) {
				COLOR.rgb = mix(COLOR.rgb, highlight_overlay.rgb, highlight_overlay.a);
			}
		}
	}

	// Границы (береговая линия + межпровинциальные) из border_mask.
	float b = texture(border_mask, UV).r;
	if (b > 0.5) {
		COLOR.rgb = mix(COLOR.rgb, border_color.rgb, 1.0);
	}

	// Обводка наведения/выбора — контур по различию id соседних пикселей.
	if (hover_id >= 0 || selected_id >= 0) {
		bool is_target = (id == hover_id + 1) || (id == selected_id + 1);
		if (is_target) {
			int l = decode_id(texture(TEXTURE, UV - vec2(texel_size.x, 0.0)));
			int r = decode_id(texture(TEXTURE, UV + vec2(texel_size.x, 0.0)));
			int t = decode_id(texture(TEXTURE, UV - vec2(0.0, texel_size.y)));
			int d = decode_id(texture(TEXTURE, UV + vec2(0.0, texel_size.y)));
			if (l != id || r != id || t != id || d != id) {
				vec4 outline = (id == selected_id + 1) ? selected_color : hover_color;
				COLOR.rgb = mix(COLOR.rgb, outline.rgb, outline.a);
			}
		}
	}
}
