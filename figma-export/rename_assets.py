import os
import shutil

PUB = r"C:\Users\Abolfazl\Desktop\Phonix\frontend\public\figma"

# friendly name -> figma imageRef
MAP = {
    "logo-phoenix": "c278a98b1d6cd7effbb4a0ed7e8824654e7125e7",  # placeholder fixed below
}

# correct refs
MAP = {
    "logo-phoenix": "c278a98b1d6cd7effbb4a0ed7e8824654e7125e7",
}

REFS = {
    "logo-phoenix": "c278a98b1d6cd7effbb4a0ed7e8824654e7125e7",
}

# Use exact refs captured from the design
ASSETS = {
    "logo-phoenix":     "c278a98b1d6cd7effbb4a0ed7e8824654e7125e7",
    "hero-tv":          "6065a63237c8bca1862deac90f2d2506ee7ca4e4",
    "hero-netflix-n":   "fbb31b90c41b94768c6a1160996481d6acd14899",
    "icon-support":     "c6754b3e10ae613657aac05b7271ed63cb445507",
    "icon-secure":      "bd6cd1dde818deda280c48dca449a90a2cef40d1",
    "cat-credit":       "b58b8ce544cbb709da37be209b62da0a61d91f46",
    "cat-graphic":      "615a4aa51c70d7614de40eb56bec1d539f4e85c7",
    "cat-film":         "3b43e6ee77e2817285fc019fa9cc7af072fbd1b7",
    "cat-music":        "015abc1b6bfc63bdf13327daad542bb4f022a384",
    "cat-more":         "745046497c2af28759b7628768f2a34a4c1d3088",
    "cat-social":       "d797dde73f36ee25a3b14f297547c303a7ca0f5d",
    "cat-games":        "865af1294dd9779115bbf29dc6eb4d656cdb036a",
    "cat-exchange":     "e80ee8797a1037754e8fcea0e36b54f4f92b4c45",
    "prod-wise":        "8d0452458bfad19d90b1d67d5fd720212158cb02",
    "logo-wise":        "a25fd25a890eaef477c0e10d91df38c37e128373",
    "prod-freelancer":  "8cd6a3ede4fbb6075c8270c6c4b0947789218a13",
    "logo-freelancer":  "49b91137c1dbbe9602a9d066382599c5962f7ffc",
    "prod-binance":     "a86b00039ef636e560ce8143480cc632e815e0a5",
    "logo-binance":     "ebffdeffa2c78105e66096ee0a648c4a595b3d49",
    "logo-binance-mark":"676d96c5dc25922b171b50cb7e5710f683c354e2",
    "prod-spotify":     "3cece94f3d6824267059d89583843d3e374a651b",
    "prod-bybit":       "583a0b2c2a138bbd21f485ff5f3d7f1d54b9604b",
    "logo-bybit":       "613b1614f825a7c8f6fddc15a49fbb7fd8fe41a9",
    "prod-applemusic":  "86fcbf68ad9181365bdc261d5b07dff53822be27",
    "logo-applemusic":  "8ef927cfa1aa20a74bd1b156aa48d9b5ecd7afc1",
    "prod-canva":       "e4bbcdbb631b7d4627c1fb3b80c9f023a08f59e4",
    "logo-canva":       "722cf74b75bbdfff820002ec49e63d8d5c3bddf6",
    "prod-netflix":     "605d3779667e7e9a1cde744da0eedeb2492e1ed3",
    "logo-netflix":     "c79b8c5c5294eecbfdf335d08498c86cf8c571fd",
    "blog-1":           "6e7c6b62853ca161fac0b207f7daa36997266122",
    "blog-2":           "dd1ac120adcc4c43fd88e974b8a868794c869101",
    "blog-3":           "7c0be353d932600f8e00ac9696d6fcf4177f77cf",
}

ok = 0
missing = []
for name, ref in ASSETS.items():
    src = os.path.join(PUB, ref + ".png")
    if os.path.exists(src):
        shutil.copyfile(src, os.path.join(PUB, name + ".png"))
        ok += 1
    else:
        missing.append((name, ref))

print(f"renamed {ok}/{len(ASSETS)}")
if missing:
    print("MISSING:")
    for n, r in missing:
        print(" ", n, r)
