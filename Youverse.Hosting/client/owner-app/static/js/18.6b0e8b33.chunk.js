"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[18,476],{8575:function(e,n,t){var a=t(6871),r=t(3504),s=t(3225),c=t(1191),i=t(184);n.Z=function(e){var n=e.className,t=e.items,o=e.isLoading,l=(0,a.s0)(),u=(null===t||void 0===t?void 0:t.length)>=6;return!0===o?(0,i.jsx)(c.Z,{className:"h-10"}):(0,i.jsxs)(i.Fragment,{children:[(0,i.jsx)("div",{className:"hidden flex-col flex-wrap ".concat(u?"":"sm:flex"," sm:flex-row ").concat(null!==n&&void 0!==n?n:""),children:t.map((function(e){return(0,i.jsx)(r.OL,{className:function(n){var t,a=n.isActive;return"flex-grow cursor-pointer border-b-2 py-2 px-1 text-lg ".concat(a?"border-indigo-500 text-indigo-500 dark:text-indigo-400":"border-gray-300 transition-colors duration-300 hover:border-indigo-400 dark:border-gray-800 hover:dark:border-indigo-600"," ").concat(null!==(t=e.className)&&void 0!==t?t:"")},to:e.path,end:!0,children:e.title},e.key)}))}),(0,i.jsx)(s.Z,{className:"".concat(u?"":"sm:hidden"," py-4"),onChange:function(e){return l(e.target.value)},value:window.location.pathname,children:t.map((function(e){return(0,i.jsx)("option",{value:e.path,children:e.text||e.title},e.key)}))})]})}},5327:function(e,n,t){t.r(n),t.d(n,{default:function(){return U}});var a=t(3433),r=t(3504),s=t(6871),c=t(4990),i=t(4165),o=t(1413),l=t(5861),u=t(7408),d=t(6752),m=t(3990),h=t(6117),f=function(){var e=arguments.length>0&&void 0!==arguments[0]?arguments[0]:{page:void 0},n=e.channelId,t=e.page,a=50,r=(0,h.Z)(),s=r.getSharedSecret,c=function(){var e=(0,l.Z)((0,i.Z)().mark((function e(){var n,t,r,c,l,u,h=arguments;return(0,i.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=h.length>0&&void 0!==h[0]?h[0]:{page:void 0},t=n.channelId,r=n.page,c=new d.KU({api:d.Ii.Owner,sharedSecret:s()}),e.next=4,c.blogPostProvider.getMasterPosts(r,a);case 4:return l=e.sent,u=(u=l.filter((function(e){return!t||e.publishTargets.some((function(e){return e.channelId===t}))}))).map((function(e){return(0,o.Z)((0,o.Z)({},e),{},{content:(0,o.Z)((0,o.Z)({},e.content),{},{itemKey:"".concat(e.content.channelId,"_").concat(e.content.id),slug:e.content.caption?(0,m.V)(e.content.caption):e.content.id})})})),e.abrupt("return",u);case 8:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return(0,u.useQuery)(["blogs",n,t],(function(){return c({channelId:n,page:t})}),{refetchOnWindowFocus:!1})},p=t(777),x=t(9072),v=t(9439),g=t(2791),j=t(9088),b=t(6916),Z=t(8491),w=t(184),N=function(){var e=(0,g.useState)(""),n=(0,v.Z)(e,2),t=n[0],a=n[1],r=(0,g.useState)(""),i=(0,v.Z)(r,2),o=i[0],l=i[1],u=(0,j.Z)().save,h=u.mutate,f=u.status,p=(0,s.s0)();return(0,w.jsx)(Z.Z,{title:"New: channel",isOpaqueBg:!0,children:(0,w.jsxs)("form",{onSubmit:function(e){e.preventDefault();var n={channelId:"",name:t,description:o,templateId:void 0,acl:{requiredSecurityGroup:d.hh.Owner}};return h(n,{onSuccess:function(){p("/owner/blog/".concat((0,m.V)(n.name)))}}),!1},children:[(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{htmlFor:"channelName",children:(0,c.t)("Name")}),(0,w.jsx)(b.Z,{id:"name",name:"channelName",onChange:function(e){a(e.target.value)},required:!0})]}),(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{htmlFor:"name",children:(0,c.t)("Description")}),(0,w.jsx)(b.Z,{id:"name",name:"channelDescription",onChange:function(e){l(e.target.value)}})]}),(0,w.jsx)("div",{className:"flex flex-row",children:(0,w.jsx)(x.Z,{className:"ml-auto",state:f,children:(0,c.t)("Add channel")})})]})})},y=function(e){var n=e.publishTargets,t=(0,p.Z)().fetch.data;if(null!==n&&void 0!==n&&n.length)return(0,w.jsx)("span",{className:"my-auto ml-2 flex flex-row flex-wrap",children:n.map((function(e){var n=t.find((function(n){return n.channelId===e.channelId}));return n?(0,w.jsx)("span",{className:"mr-2 bg-green-300 px-2 py-1 text-xs",title:"".concat((0,c.t)("Published to"),": ").concat(n.name),children:n.name},n.channelId):null}))})},k=function(e){var n,t=e.className,a=e.blog,s=e.linkRoot;return(0,w.jsx)(r.rU,{to:"".concat(s).concat(null!==(n=a.content.slug)&&void 0!==n?n:"#"),className:"contents",children:(0,w.jsx)("div",{className:"flex flex-nowrap px-5 py-8 transition-colors duration-200 hover:bg-slate-50 hover:dark:bg-slate-800 ".concat(null!==t&&void 0!==t?t:""),children:(0,w.jsxs)("div",{className:"flex flex-col md:flex-grow md:flex-row",children:[(0,w.jsxs)("div",{className:"flex flex-shrink-0 md:order-3 md:mb-0 md:mt-3 md:w-32 md:flex-col md:text-right lg:w-64",children:[(0,w.jsx)("span",{className:"title-font font-semibold text-gray-700 dark:text-white",children:a.content.type.toUpperCase()}),(0,w.jsx)("span",{className:"pl-2 text-gray-500 md:mt-1 md:pl-0 md:text-sm",children:new Date(a.content.dateUnixTime).toLocaleDateString()})]}),(0,w.jsxs)("div",{className:"md:order-2 md:flex-grow",children:[(0,w.jsxs)("h2",{className:"title-font mb-2 flex flex-row text-2xl font-medium text-gray-900 dark:text-white",children:[a.content.caption,(0,w.jsx)(y,{publishTargets:a.publishTargets})]}),"abstract"in a.content?(0,w.jsx)("p",{className:"leading-relaxed",children:a.content.abstract}):null]})]})})})},C=t(1191),I=t(340),S=t(3225),D=t(2465),O=function(e){var n=e.channel,t=e.onCancel,a=(0,g.useState)((0,o.Z)({},n)),r=(0,v.Z)(a,2),i=r[0],l=r[1],u=(0,j.Z)(),h=u.save,f=h.mutate,p=h.status,N=u.remove,y=N.mutate,k=N.status,C=(0,s.s0)();if(n){var O=function(e){var n=(0,o.Z)({},i);n[e.target.name]=e.target.value,l(n)},F=Object.keys(d.rJ).filter((function(e){return!isNaN(Number(e))})),L=Object.keys(d.rJ).filter((function(e){return isNaN(Number(e))}));return(0,w.jsx)(Z.Z,{title:(0,w.jsxs)(w.Fragment,{children:[(0,w.jsx)(I.Z,{acl:i.acl,onChange:function(e){O({target:{name:"acl",value:e}})}},i.channelId)," ","".concat((0,c.t)("Edit"),": ").concat(n.name)]}),isOpaqueBg:!0,children:(0,w.jsxs)("form",{onSubmit:function(e){e.preventDefault(),f(i,{onSuccess:function(){C("/owner/blog/".concat((0,m.V)(i.name)))}})},children:[(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{htmlFor:"name",children:(0,c.t)("Name")}),(0,w.jsx)(b.Z,{id:"name",name:"name",defaultValue:n.name,onChange:O})]}),(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{htmlFor:"description",children:(0,c.t)("Description")}),(0,w.jsx)(D.Z,{id:"description",name:"description",defaultValue:n.description,onChange:O})]}),(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{htmlFor:"template",children:(0,c.t)("Template")}),(0,w.jsxs)(S.Z,{id:"template",name:"templateId",defaultValue:n.templateId,onChange:O,children:[(0,w.jsx)("option",{children:(0,c.t)("Make a selection")}),F.map((function(e,n){return(0,w.jsx)("option",{value:e,children:(0,c.t)(L[n])},e)}))]})]}),(0,w.jsxs)("div",{className:"-m-2 flex flex-row-reverse",children:[(0,w.jsx)(x.Z,{type:"secondary",className:"m-2",onClick:function(e){e.preventDefault(),t()},children:(0,c.t)("Cancel")}),(0,w.jsx)(x.Z,{type:"primary",className:"m-2",state:p,children:(0,c.t)("Save")}),(0,w.jsx)(x.Z,{type:"remove",icon:"trash",className:"m-2 mr-auto",state:k,onClick:function(){return y(n)},confirmOptions:{title:(0,c.t)("Remove channel"),body:(0,c.t)("Are you sure you want to remove this channel, this action cannot be undone. All blogs published on this channel will also be unpublished."),buttonText:(0,c.t)("Remove")},children:(0,c.t)("Remove")})]})]})})}},F=function(e){var n=e.channel,t=e.blogs,a=e.isParentLoading,r="/owner/blog/".concat(n?n.slug:"all","/"),s=(0,g.useState)(!1),i=(0,v.Z)(s,2),o=i[0],l=i[1];return a?(0,w.jsxs)("div",{className:"-m-5 pt-5",children:[(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"}),(0,w.jsx)(C.Z,{className:"m-5 h-20"})]}):t?(0,w.jsxs)(w.Fragment,{children:[n?o?(0,w.jsx)(O,{channel:n,onCancel:function(){return l(!1)}},n.channelId):(0,w.jsxs)("section",{className:"items-center bg-slate-50 p-3 dark:bg-slate-800 sm:flex sm:flex-row",children:[(0,w.jsx)("p",{className:"sm:mr-2",children:n.description?n.description:n.name}),(0,w.jsxs)("p",{className:"ml-auto",children:[(0,c.t)("Template"),":"," ",parseInt(n.templateId+"")===d.rJ.LargeCards?(0,c.t)("LargeCards"):parseInt(n.templateId+"")===d.rJ.ClassicBlog?(0,c.t)("ClassicBlog"):(0,c.t)("MasonryLayout")]}),(0,w.jsx)(x.Z,{type:"secondary",className:"sm:ml-2",onClick:function(){return l(!0)},children:(0,c.t)("Edit Channel")})]}):null,t.length?(0,w.jsx)("div",{className:"-mx-5 divide-y-2 divide-gray-100 dark:divide-gray-800",children:t.map((function(e){return(0,w.jsx)(k,{blog:e,linkRoot:r},e.content.itemKey)}))}):(0,w.jsx)("div",{className:"mt-4",children:(0,c.t)("no-data-found")})]}):(0,w.jsx)(w.Fragment,{})},L=t(5207),Q=t(4439),P=t(3004),T=t(8575),U=function(){var e=(0,s.UO)().channelKey,n=(0,p.Z)().fetch,t=n.data,i=n.isLoading,o=null===t||void 0===t?void 0:t.find((function(n){return n.slug===e})),l=f({channelId:null===o||void 0===o?void 0:o.channelId,page:void 0}),u=l.data,d=l.isLoading;return(0,w.jsxs)("section",{children:[(0,w.jsx)(P.Z,{icon:Q.Z,title:(0,c.t)("Blog"),actions:(0,w.jsx)(r.rU,{className:"contents",to:"/owner/blog/draft/new",children:(0,w.jsx)(x.Z,{type:"primary",icon:"plus",className:"m-2",children:(0,c.t)("New")})})}),(0,w.jsx)(T.Z,{items:[{title:(0,c.t)("Drafts"),key:"",path:"/owner/blog"}].concat((0,a.Z)((null!==t&&void 0!==t?t:[]).map((function(e){return{title:e.name,key:e.channelId,path:"/owner/blog/".concat(e.slug)}}))),[{title:(0,w.jsx)(L.Z,{className:"h-5 w-5"}),text:"-- ".concat((0,c.t)("Create new channel")," --"),key:"new",className:"flex-grow-0",path:"/owner/blog/new"}]),isLoading:i}),(0,w.jsx)("div",{className:"pt-5",children:"new"===e?(0,w.jsx)(N,{}):(0,w.jsx)(F,{channel:o,blogs:u,isParentLoading:i||d})})]})}},3990:function(e,n,t){t.d(n,{P:function(){return r},V:function(){return a}});var a=function(e){return e.split(" ").join("-").toLowerCase()},r=function(e){return Object.keys(e).map((function(n){return n+"="+e[n]})).join("&")}},9088:function(e,n,t){var a=t(4165),r=t(5861),s=t(7408),c=t(6752),i=t(6117),o=t(1695);n.Z=function(){var e=arguments.length>0&&void 0!==arguments[0]?arguments[0]:{},n=e.channelId,t=(0,i.Z)(),l=t.getSharedSecret,u=new c.KU({api:c.Ii.Owner,sharedSecret:l()}),d=(0,s.useQueryClient)(),m=(0,o.Z)().publish.mutate,h=function(){var e=(0,r.Z)((0,a.Z)().mark((function e(n){var t;return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(n){e.next=2;break}return e.abrupt("return");case 2:return e.next=4,u.blogDefinitionProvider.getChannelDefinition(n);case 4:return t=e.sent,e.abrupt("return",t);case 6:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}(),f=function(){var e=(0,r.Z)((0,a.Z)().mark((function e(n){return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,u.blogDefinitionProvider.saveChannelDefinition(n);case 2:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}(),p=function(){var e=(0,r.Z)((0,a.Z)().mark((function e(n){return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,u.blogDefinitionProvider.removeChannelDefinition(n.channelId);case 2:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}();return{fetch:(0,s.useQuery)(["channel",n],(function(){return h(n)}),{refetchOnWindowFocus:!1}),save:(0,s.useMutation)(f,{onSuccess:function(e,n){n.channelId?d.removeQueries(["channel",n.channelId]):d.removeQueries(["channel"]),d.removeQueries(["channels"]),m()}}),remove:(0,s.useMutation)(p,{onMutate:function(){var e=(0,r.Z)((0,a.Z)().mark((function e(n){var t,r;return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,d.cancelQueries(["channels"]);case 2:return t=d.getQueryData(["channels"]),r=null===t||void 0===t?void 0:t.filter((function(e){return e.channelId!==n.channelId})),d.setQueryData(["channels"],r),e.abrupt("return",{previousChannels:t,toRemoveChannel:n});case 6:case"end":return e.stop()}}),e)})));return function(n){return e.apply(this,arguments)}}(),onError:function(e,n,t){console.error(e),d.setQueryData(["channels"],t.previousChannels)},onSettled:function(){d.invalidateQueries(["channels"])}})}}},777:function(e,n,t){var a=t(4165),r=t(1413),s=t(5861),c=t(7408),i=t(6752),o=t(3990),l=t(6117);n.Z=function(){var e=(0,l.Z)().getSharedSecret,n=new i.KU({api:i.Ii.Owner,sharedSecret:e()}),t=function(){var e=(0,s.Z)((0,a.Z)().mark((function e(){var t;return(0,a.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,n.blogDefinitionProvider.getChannelDefinitions();case 2:return t=e.sent,e.abrupt("return",t.map((function(e){return(0,r.Z)((0,r.Z)({},e),{},{slug:(0,o.V)(e.name)})})));case 4:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return{fetch:(0,c.useQuery)(["channels"],(function(){return t()}),{refetchOnWindowFocus:!1})}}}}]);
//# sourceMappingURL=18.6b0e8b33.chunk.js.map