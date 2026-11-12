//! Level and trail files, read exactly as `Crawler.Protocol.LevelCodec` and `TrailCodec`
//! read them. Both files are part of the contract with the client.

use std::fs;
use std::path::{Path, PathBuf};

use serde::Deserialize;

#[derive(Debug, Clone)]
pub struct Region {
    pub name: String,
    pub subtitle: String,
    pub rule: String,
    /// (x, y) is the south-west corner, in cells.
    pub x: i32,
    pub y: i32,
    pub w: i32,
    pub h: i32,
}

impl Region {
    pub fn contains(&self, x: i32, y: i32) -> bool {
        x >= self.x && y >= self.y && x < self.x + self.w && y < self.y + self.h
    }
}

/// A lever or plate driving a door, in world cells (x from the west, y from the south).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Link {
    pub from: (i32, i32),
    pub to: (i32, i32),
}

#[derive(Debug, Clone)]
pub struct Level {
    pub id: String,
    pub name: String,
    pub biome: String,
    pub width: i32,
    pub height: i32,
    /// `tiles[y][x]`, y = 0 is the southern row.
    tiles: Vec<Vec<char>>,
    pub regions: Vec<Region>,
    pub links: Vec<Link>,
    pub mechanics: Vec<String>,
    /// Cells that are ice beneath an entity glyph (a key on the lake), from `"ice"`.
    pub ice_under: Vec<(i32, i32)>,
}

#[derive(Deserialize)]
struct RawLevel {
    id: Option<String>,
    name: Option<String>,
    biome: Option<String>,
    width: Option<i32>,
    height: Option<i32>,
    grid: Option<i32>,
    rows: Vec<String>,
    #[serde(default)]
    regions: Vec<RawRegion>,
    #[serde(default)]
    links: Vec<RawLink>,
    #[serde(default)]
    mechanics: Vec<String>,
    #[serde(default)]
    ice: Vec<Vec<i32>>,
}

#[derive(Deserialize)]
struct RawRegion {
    #[serde(default)]
    name: String,
    #[serde(default)]
    subtitle: String,
    #[serde(default)]
    rule: String,
    rect: Vec<i32>,
}

#[derive(Deserialize)]
struct RawLink {
    from: Vec<i32>,
    to: Vec<i32>,
}

/// Level ids are whitelisted because the server sends them and the client opens files by them.
pub fn valid_id(id: &str) -> bool {
    (1..=32).contains(&id.len())
        && id.bytes().all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'_' || b == b'-')
}

impl Level {
    pub fn parse(json: &str) -> Result<Level, String> {
        let raw: RawLevel = serde_json::from_str(json).map_err(|e| format!("bad level json: {e}"))?;
        let rows = raw.rows;
        if rows.is_empty() {
            return Err("level has no rows".into());
        }
        let height = raw.height.or(raw.grid).unwrap_or(rows.len() as i32);
        let width = raw.width.or(raw.grid).unwrap_or(rows[0].chars().count() as i32);
        if width <= 0 || height <= 0 {
            return Err("level size must be positive".into());
        }
        if rows.len() as i32 != height {
            return Err(format!("level has {} rows, expected {height}", rows.len()));
        }

        let mut tiles = vec![Vec::new(); height as usize];
        for (r, row) in rows.iter().enumerate() {
            let chars: Vec<char> = row.chars().collect();
            if chars.len() as i32 != width {
                return Err(format!("row {r} has {} chars, expected {width}", chars.len()));
            }
            tiles[(height - 1 - r as i32) as usize] = chars;
        }

        let id = raw.id.ok_or("level has no id")?;
        if !valid_id(&id) {
            return Err(format!("level id '{id}' is not [a-z0-9_-]{{1,32}}"));
        }

        let mut level = Level {
            name: raw.name.unwrap_or_else(|| id.clone()),
            id,
            biome: raw.biome.unwrap_or_default(),
            width,
            height,
            tiles,
            regions: Vec::new(),
            links: Vec::new(),
            mechanics: raw.mechanics,
            ice_under: Vec::new(),
        };

        for r in raw.regions {
            let [x, y, w, h] = r.rect[..] else {
                return Err("region rect must be [x, y, w, h]".into());
            };
            level.regions.push(Region { name: r.name, subtitle: r.subtitle, rule: r.rule, x, y, w, h });
        }

        // Links are [column, row] as read in "rows" (row 0 is the top line).
        for l in raw.links {
            let ([fc, fr], [tc, tr]) = (&l.from[..], &l.to[..]) else {
                return Err("link must be { \"from\": [col, row], \"to\": [col, row] }".into());
            };
            let link = Link { from: (*fc, height - 1 - fr), to: (*tc, height - 1 - tr) };
            if !level.in_bounds(link.from.0, link.from.1) || !level.in_bounds(link.to.0, link.to.1) {
                return Err("link points outside the map".into());
            }
            level.links.push(link);
        }

        // Extra ice cells, [column, row] like links.
        for cell in raw.ice {
            let [c, r] = cell[..] else {
                return Err("an ice cell must be [col, row]".into());
            };
            let at = (c, height - 1 - r);
            if !level.in_bounds(at.0, at.1) {
                return Err("an ice cell is outside the map".into());
            }
            level.ice_under.push(at);
        }
        Ok(level)
    }

    /// Ice: a '~' cell, or a cell the level lists under `"ice"`.
    pub fn is_ice(&self, x: i32, y: i32) -> bool {
        self.at(x, y) == '~' || self.ice_under.contains(&(x, y))
    }

    pub fn in_bounds(&self, x: i32, y: i32) -> bool {
        x >= 0 && y >= 0 && x < self.width && y < self.height
    }

    /// The glyph at a cell; outside the map reads as wall.
    pub fn at(&self, x: i32, y: i32) -> char {
        if self.in_bounds(x, y) { self.tiles[y as usize][x as usize] } else { '#' }
    }

    pub fn is_solid(c: char) -> bool {
        c == '#' || c == 'T'
    }

    /// Index of the first region containing the cell, or -1.
    pub fn region_at(&self, x: i32, y: i32) -> i32 {
        self.regions.iter().position(|r| r.contains(x, y)).map_or(-1, |i| i as i32)
    }

    pub fn rule_of(&self, region: i32) -> &str {
        if region >= 0 { self.regions.get(region as usize).map_or("", |r| r.rule.as_str()) } else { "" }
    }

    /// The first 'E' cell inside a region, scanning from its south-west corner.
    pub fn entrance_of(&self, region: i32) -> Option<(i32, i32)> {
        let r = self.regions.get(usize::try_from(region).ok()?)?;
        for y in r.y..r.y + r.h {
            for x in r.x..r.x + r.w {
                if self.at(x, y) == 'E' {
                    return Some((x, y));
                }
            }
        }
        None
    }

    /// Cells in row-scan order: top row first, west to east, as an author reads the map.
    pub fn scan(&self) -> impl Iterator<Item = (i32, i32, char)> + '_ {
        (0..self.height).rev().flat_map(move |y| (0..self.width).map(move |x| (x, y, self.at(x, y))))
    }

    pub fn find(&self, glyph: char) -> Option<(i32, i32)> {
        self.scan().find(|&(_, _, c)| c == glyph).map(|(x, y, _)| (x, y))
    }
}

#[derive(Debug, Clone, Deserialize)]
pub struct Biome {
    pub id: String,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub levels: Vec<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Trail {
    pub biomes: Vec<Biome>,
}

impl Trail {
    pub fn parse(json: &str) -> Result<Trail, String> {
        serde_json::from_str(json).map_err(|e| format!("bad trail json: {e}"))
    }

    /// The level after this one, crossing into the next biome at a finale; None at the end.
    pub fn next(&self, id: &str) -> Option<&str> {
        for (bi, b) in self.biomes.iter().enumerate() {
            let Some(i) = b.levels.iter().position(|l| l == id) else { continue };
            if let Some(n) = b.levels.get(i + 1) {
                return Some(n);
            }
            return self.biomes[bi + 1..].iter().find_map(|nb| nb.levels.first().map(String::as_str));
        }
        None
    }
}

/// Loads levels and the trail from a directory (the client's StreamingAssets/levels).
#[derive(Debug, Clone)]
pub struct LevelStore {
    dir: PathBuf,
}

impl LevelStore {
    pub fn new(dir: impl Into<PathBuf>) -> Self {
        LevelStore { dir: dir.into() }
    }

    pub fn dir(&self) -> &Path {
        &self.dir
    }

    pub fn load(&self, id: &str) -> Result<Level, String> {
        if !valid_id(id) {
            return Err(format!("level id '{id}' is not [a-z0-9_-]{{1,32}}"));
        }
        let path = self.dir.join(format!("{id}.json"));
        let text = fs::read_to_string(&path).map_err(|e| format!("{}: {e}", path.display()))?;
        let level = Level::parse(&text).map_err(|e| format!("{id}: {e}"))?;
        if level.id != id {
            return Err(format!("{}: file says id '{}'", path.display(), level.id));
        }
        Ok(level)
    }

    pub fn trail(&self) -> Result<Trail, String> {
        let path = self.dir.join("trail.json");
        let text = fs::read_to_string(&path).map_err(|e| format!("{}: {e}", path.display()))?;
        Trail::parse(&text)
    }
}
