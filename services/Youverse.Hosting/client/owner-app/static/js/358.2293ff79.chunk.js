"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[358],{9358:function(e,t,r){r.r(t),r.d(t,{default:function(){return j}});var n=r(9631),i=r(4990),a=r(4165),s=r(1413),o=r(5861),u=r(7408),c=r(8609),l=function(){var e=(0,c.ZP)().getSharedSecret,t=new n.KU({api:n.Ii.Owner,sharedSecret:e()}),r=function(){var e=(0,o.Z)((0,a.Z)().mark((function e(){var r;return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,t.homePageProvider.getAttributes(n.Dg.HomePage);case 2:return r=e.sent.map((function(e){return(0,s.Z)((0,s.Z)({},e),{},{typeDefinition:{type:n.Dg.HomePage,name:"Home Page",description:""}})})),e.abrupt("return",r);case 4:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),i=function(){var e=(0,o.Z)((0,a.Z)().mark((function e(){var r;return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,t.homePageProvider.getAttributes(n.Dg.Theme);case 2:return r=e.sent.map((function(e){return(0,s.Z)((0,s.Z)({},e),{},{typeDefinition:{type:n.Dg.Theme,name:"Theme",description:""}})})),e.abrupt("return",r);case 4:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return{fetchHome:(0,u.useQuery)(["attributes",n.Ib.DefaultDriveId,n.Dg.HomePage],r,{}),fetchTheme:(0,u.useQuery)(["attributes",n.Ib.DefaultDriveId,n.Dg.Theme],i,{})}},p=r(1695),d=r(2042),m=r(9037),h=r(2851),f=r(8137),g=r(9080),b=r(4411),v=r(1434),x=r(184),D={id:void 0,profileId:n.Ib.DefaultDriveId,type:n.Dg.HomePage,priority:1e3,sectionId:n.Ib.AttributeSectionNotApplicable,data:{},acl:void 0,typeDefinition:{type:n.Dg.HomePage,name:"Home Page",description:""}},Z={id:void 0,profileId:n.Ib.DefaultDriveId,type:n.Dg.Theme,priority:1e3,sectionId:n.Ib.AttributeSectionNotApplicable,data:{},acl:void 0,typeDefinition:{type:n.Dg.Theme,name:"Theme",description:""}},y=function(){var e=(0,p.Z)().publish,t=e.mutate,r=e.status,n=e.error;return(0,x.jsxs)(x.Fragment,{children:[(0,x.jsx)(d.Z,{error:n}),(0,x.jsx)(v.Z,{isOpaqueBg:!0,title:(0,x.jsxs)(x.Fragment,{children:[(0,x.jsx)(f.Z,{className:"inline-block h-4 w-4"})," ",(0,i.t)("Publish your public data")]}),children:(0,x.jsx)("div",{className:"flex flex-row",children:(0,x.jsx)(h.Z,{onClick:function(){return t()},state:r,icon:"save",className:"ml-auto",children:(0,i.t)("Publish static file")})})})]})},j=function(){var e=l().fetchHome,t=e.data,r=e.isLoading,n=l().fetchTheme,a=n.data,s=n.isLoading;return(0,x.jsxs)("section",{children:[(0,x.jsx)(g.Z,{icon:f.Z,title:(0,i.t)("Website Configuration")}),r?(0,x.jsx)("div",{className:"-m-5 pt-5",children:(0,x.jsx)(b.Z,{className:"m-5 h-20"})}):t?(0,x.jsx)(m.Z,{attributes:null!==t&&void 0!==t&&t.length?t:[D],groupTitle:(0,i.t)("Home")}):null,s?(0,x.jsx)("div",{className:"-m-5 pt-5",children:(0,x.jsx)(b.Z,{className:"m-5 h-20"})}):a?(0,x.jsx)(m.Z,{attributes:null!==a&&void 0!==a&&a.length?a:[Z],groupTitle:(0,i.t)("Theme")}):null,(0,x.jsx)(y,{})]})}}}]);
//# sourceMappingURL=358.2293ff79.chunk.js.map