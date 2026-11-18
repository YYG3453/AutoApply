// 体检探针：给平台健康页用的同一段判定逻辑，命令行可单独验证
(function () {
  var t = document.body ? document.body.innerText : '';
  var cap = /滑动验证|访问验证|请按住滑块|拖动到最右边|安全验证|验证码/.test(t);
  var login = /扫码登录|登录\/注册|立即登录|请先登录|登录后查看/.test(t);
  var ok = /我的简历|在线简历|个人中心|我的申请|消息|退出登录|已投递|职位|搜索/.test(t);
  return JSON.stringify({ url: location.href, len: t.length, cap: cap, login: login && !ok, head: (document.title || '').slice(0, 40) });
})()
