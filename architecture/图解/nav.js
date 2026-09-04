// 架构图解 · 导航高亮脚本（可选增强：无此脚本页面依旧可完整阅读与跳转）
(function () {
  try {
    var here = location.pathname.split("/").pop() || "index.html";
    var links = document.querySelectorAll(".menu a, .topnav .prevnext a");
    for (var i = 0; i < links.length; i++) {
      var href = links[i].getAttribute("href");
      if (href && href.split("/").pop() === here) {
        links[i].classList.add("current");
      }
    }
  } catch (e) {
    /* 静默失败，不影响页面阅读 */
  }
})();
