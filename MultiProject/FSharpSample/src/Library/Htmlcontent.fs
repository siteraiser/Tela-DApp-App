module Htmlcontent

open Security
let searchHTMLOpen = """<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
html, body {
  margin:0;
  padding:0;
  background:#222;
  color:#eee;
}
a {color:lightgreen}



header {
  background:#244;
  padding:5px;
  display: flex;
  justify-content: space-around;
  flex-direction: row;
  flex-wrap: wrap;
  column-gap: 1em;
}
header div {
    display: flex;
    justify-content: center;   
    align-items: center; 
    gap: 0.5rem;
    flex-wrap: wrap; 
    margin-bottom: 1rem; 
}


header input[type="text"], header button  {
  color: #eee;
  background:#ffffff00;
  border: 2px solid gray;    
  padding: 0.4rem 0.6rem;
  font-size: 1rem;
}

header input[type="text"]:focus {
  color: #eef; 
  border-color: blue; 
}

header button {
    padding: 0.4rem 0.8rem;
    font-size: 1rem;
    cursor: pointer;
    border-radius: 5px;
}

main > div {
  margin:3px;
  padding: 5px 8px;
  background:#244;
  border: solid 1px #000b53;
  border-radius: 5px;
}

.hidden{display:none;}

</style>
</head>
<body>
<header>
<div>
<input id="scid_input" type="text" placeholder="Tela Index SCID">
<button id="load_button">Load SCID</button> 
</div>
<div>
<input id="owner_input" type="text" placeholder="Enter anon or Dero Address">
<button id="owner_button">Filter by Owner</button>
</div>
</header>

<main>
"""

let searchHTMLCloseTemplate = """
</main>
<script>
const AUTH_TOKEN = "{{TOKEN}}";

document.addEventListener("DOMContentLoaded", () => {

    // Create event handler for manual SCID loads
    const scidInput = document.getElementById("scid_input");
    const loadButton = document.getElementById("load_button");
    loadButton.addEventListener("click", e => {
            const scid = scidInput.value;
            launch(scid);
     });

    // Create event handler to filter by address
    const ownerInput = document.getElementById("owner_input");
    const ownerButton = document.getElementById("owner_button");
    // Add hidden class to all addresses that don't match the supplied address
    ownerButton.addEventListener("click", e => {
        const owner = ownerInput.value.trim();
        const links = document.querySelectorAll("a[data-owner]");

        links.forEach(link => {
            const linkOwner = link.getAttribute("data-owner");

            if (owner === "" || linkOwner === owner) {
                link.parentElement.parentElement.classList.remove("hidden");
            } else {
                link.parentElement.parentElement.classList.add("hidden");
            }
        });
    });


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