module Htmlcontent

open Security
let searchHTMLOpen = """<!doctype html>
<html>
<body>
"""

let searchHTMLCloseTemplate = """
<script>
const AUTH_TOKEN = "{{TOKEN}}";
document.addEventListener("DOMContentLoaded", () => {
    const links = document.getElementsByTagName("a");

    // Convert HTMLCollection → Array so we can loop cleanly
    Array.from(links).forEach(link => {
        link.addEventListener("click", e => {
            e.preventDefault(); // stop normal navigation
            const scid = link.getAttribute("data-scid");
            launch(scid);
        });
    });
});

function launch(scid) {
    fetch(
      `/tela/open/${scid}`,
      {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
            'X-Launcher-Token': AUTH_TOKEN
        }
      }
    )
    .then(r => r.json())
    .then(data => {
        console.log("Backend response:", data);
        // If backend launches browser, nothing else needed
        // If backend returns a URL, you can open it:
        // window.open(data.url, "_blank");
    })
    .catch(err => console.error("Launch error:", err));
}
</script>

</body>
</html>
"""
let searchHTMLClose =
    searchHTMLCloseTemplate
        .Replace("{{TOKEN}}", getToken())
       



(*

{
  "sc_code": "Initialize() .... /* the actual doc content... */",
  "variables": [
    {
      "Key": "hash",
      "Value": "ed9fe3f468ea256bdeb0d205dcb68f23bcb6e4ba5e4a61898449794116292a91"
    },
    {
      "Key": "iconURLHdr",
      "Value": ""
    },
    {
      "Key": "dislikes",
      "Value": 0
    },
    {
      "Key": "likes",
      "Value": 0
    },
    {
      "Key": "docType",
      "Value": "TELA-HTML-1"
    },
    {
      "Key": "fileCheckS",
      "Value": "7d0814fc01680ab1cd4dc6090ce844d8934ddb7a081c00f55f4c259a26ec20b"
    },
    {
      "Key": "subDir",
      "Value": ""
    },
    {
      "Key": "C",
      "Value": "Initialize() .... /* the actual doc content... */",
    },
    {
      "Key": "dURL",
      "Value": "index.html"
    },
    {
      "Key": "descrHdr",
      "Value": "Index"
    },
    {
      "Key": "docVersion",
      "Value": "1.0.0"
    },
    {
      "Key": "nameHdr",
      "Value": "index.html"
    },
    {
      "Key": "owner",
      "Value": "dero1qy4yf7c577wqzvzq44x30lygha5wjz7wrhw6dnv5n3pq4f4d7aqluqq30m7ak"
    },
    {
      "Key": "fileCheckC",
      "Value": "1e4a19ad58a9ddd78e775d310bfa1a18482d97bc0bd15527146c5419ae32327a"
    }
  ]
}
*)