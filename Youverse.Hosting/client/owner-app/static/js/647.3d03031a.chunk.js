"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[647],{43:function(e,n,t){t.r(n),t.d(n,{default:function(){return F}});var r=t(1413),i=t(2982),o=t(885),a=t(2791),s=t(6871),c=t(4165),u=t(5861),l=t(7408),d=t(6752),f=t(6117),p=function(e){var n=e.profileId,t=e.sectionId,i=(0,f.Z)().getSharedSecret,o=new d.KU({api:d.Ii.Owner,sharedSecret:i()}),a=function(){var e=(0,u.Z)((0,c.Z)().mark((function e(n,t){var i;return(0,c.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,o.profileDataProvider.getProfileAttributes(n,t,1,100);case 2:return i=e.sent,e.abrupt("return",i.map((function(e){return(0,r.Z)((0,r.Z)({},e),{},{typeDefinition:Object.values(d.H1).find((function(n){return n.type.toString()===e.type}))})})));case 4:case"end":return e.stop()}}),e)})));return function(n,t){return e.apply(this,arguments)}}();return[(0,l.useQuery)(["attributes",n,t],(function(){return a(n,t)}),{refetchOnMount:!1,refetchOnWindowFocus:!1})]},m=t(3431),h=t(184),x=function(e){var n=e.className,t=e.items,r=e.onChange;return(0,h.jsx)("div",{className:"flex ".concat(n),children:t.map((function(e){var n;return(0,h.jsx)("a",{className:"flex-grow cursor-pointer border-b-2 py-2 px-1 text-lg ".concat(e.isActive?"border-indigo-500 text-indigo-500 dark:text-indigo-400":"border-gray-300 transition-colors duration-300 hover:border-indigo-400 dark:border-gray-800 hover:dark:border-indigo-600"," ").concat(null!==(n=e.className)&&void 0!==n?n:""),onClick:function(){r(e.key)},children:e.title},e.key)}))})},v=t(4990),g=t(9077),y=t(8191),j=t(3225),Z=t(8491),b=t(4226),w=function(e){var n,t=e.profileId,i=e.sectionId,s=(0,a.useState)(!1),c=(0,o.Z)(s,2),u=c[0],l=c[1],f=(0,a.useState)(),p=(0,o.Z)(f,2),m=p[0],x=p[1],w=(0,g.Z)({}),I=(0,o.Z)(w,2)[1],N=I.mutate,S=I.isLoading,k=I.isError,O=I.isSuccess,C=function(){l(!1),x(void 0)};return(0,h.jsx)(h.Fragment,{children:u?(0,h.jsxs)(Z.Z,{title:"New".concat(m?":":""," ").concat(null!==(n=null===m||void 0===m?void 0:m.typeDefinition.name)&&void 0!==n?n:""),isOpaqueBg:!0,children:[void 0===m?(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"type",children:(0,v.t)("Attribute Type")}),(0,h.jsxs)(j.Z,{id:"type",onChange:function(e){!function(e){var n=Object.values(d.H1).find((function(n){return n.type.toString()===e}));x({id:"",type:e,sectionId:i,priority:-1,data:{},typeDefinition:n,profileId:t,acl:{requiredSecurityGroup:d.hh.Owner}})}(e.target.value)},children:[(0,h.jsx)("option",{children:(0,v.t)("Make a selection")}),Object.values(d.H1).map((function(e){return(0,h.jsx)("option",{value:e.type.toString(),children:e.name},e.type.toString())}))]})]}):(0,h.jsx)(b.Z,{attribute:m,onChange:function(e){if(m){var n=(0,r.Z)({},m);n.data[e.target.name]=e.target.value,x(n)}}}),(0,h.jsxs)("div",{className:"flex flex-row",children:[(0,h.jsx)(y.Z,{type:"secondary",className:"ml-auto",onClick:C,children:(0,v.t)("Cancel")}),(0,h.jsx)(y.Z,{type:"primary",icon:"plus",className:"ml-2",onClick:function(){console.log(m),N(m,{onSuccess:function(){C()}})},state:S?"loading":O?"success":k?"failed":void 0,children:(0,v.t)("Add")})]})]}):(0,h.jsx)("div",{className:"flex flex-row",children:(0,h.jsx)(y.Z,{type:"primary",icon:"plus",className:"mx-auto min-w-[9rem]",onClick:function(){return l(!0)},children:(0,v.t)("Add Attribute")})})})},I=t(6916),N=t(5207),S=t(3004),k=t(2864),O=function(e){var n=e.profileDefinition,t=(0,a.useState)(""),s=(0,o.Z)(t,2),c=s[0],u=s[1];return(0,h.jsx)(Z.Z,{title:"New: section",isOpaqueBg:!0,children:(0,h.jsxs)("form",{onSubmit:function(e){e.preventDefault();var t={sectionId:"",attributes:[],priority:Math.max.apply(Math,(0,i.Z)(n.sections.map((function(e){return e.priority}))))+1,isSystemSection:!1,name:c},o=(0,r.Z)({},n);return o.sections.push(t),console.log("Should create: ",o),!1},children:[(0,h.jsxs)("div",{className:"mb-5",children:[(0,h.jsx)("label",{htmlFor:"name",children:(0,v.t)("Name")}),(0,h.jsx)(I.Z,{id:"name",name:"sectionName",onChange:function(e){u(e.target.value)},required:!0})]}),(0,h.jsx)("div",{className:"flex flex-row",children:(0,h.jsx)(y.Z,{className:"ml-auto",children:(0,v.t)("add section")})})]})})},C=function(e){var n=e.section,t=e.profileId,r=p({profileId:t,sectionId:n.sectionId}),a=(0,o.Z)(r,1)[0],s=a.data,c=a.isLoading;if(!s||c)return(0,h.jsx)(h.Fragment,{children:"Loading"});var u=s.reduce((function(e,n){return-1!==e.indexOf(n.type)?e:[].concat((0,i.Z)(e),[n.type])}),[]).map((function(e){var n=s.filter((function(n){return n.type===e}));return{name:n[0].typeDefinition.name,attributes:n}}));return(0,h.jsxs)(h.Fragment,{children:[s.length?u.map((function(e){return(0,h.jsx)(k.Z,{groupTitle:e.name,attributes:e.attributes},e.name)})):(0,h.jsx)("div",{className:"py-5",children:(0,v.t)("section-empty-attributes")}),(0,h.jsx)(w,{profileId:t,sectionId:n.sectionId})]})},F=function(){var e=(0,m.Z)(),n=e.data,t=e.isLoading,r=(0,s.UO)().profileKey,c=null===n||void 0===n?void 0:n.definitions.find((function(e){return e.slug===r})),u=(0,a.useState)(null!==c&&void 0!==c&&c.sections?c.sections[0].sectionId:""),l=(0,o.Z)(u,2),d=l[0],f=l[1];if(t)return(0,h.jsx)(h.Fragment,{children:"Loading"});if(!n)return(0,h.jsx)(h.Fragment,{children:(0,v.t)("no-data-found")});if(!c)return(0,h.jsx)(h.Fragment,{children:"Incorrect profile path"});var p="new"===d?void 0:c.sections.find((function(e){return e.sectionId===d}))||c.sections[0];return(0,h.jsxs)(h.Fragment,{children:[(0,h.jsx)(S.Z,{title:c.name}),(0,h.jsx)(x,{className:"mt-5",items:[].concat((0,i.Z)(c.sections.map((function(e,n){return{title:e.name,key:e.sectionId,isActive:d?d===e.sectionId:0===n}}))),[{title:(0,h.jsx)(N.Z,{className:"h-5 w-5"}),key:"new",isActive:d?"new"===d:!c.sections.length,className:"flex-grow-0"}]),onChange:function(e){f(e)}}),"new"===d?(0,h.jsx)(O,{profileDefinition:c}):p&&(0,h.jsx)(C,{section:p,profileId:c.profileId},p.sectionId)]})}},3990:function(e,n,t){t.d(n,{V:function(){return r}});var r=function(e){return e.split(" ").join("-").toLowerCase()}},3431:function(e,n,t){var r=t(4165),i=t(1413),o=t(5861),a=t(7408),s=t(6752),c=t(3990),u=t(6117);n.Z=function(){var e=(0,u.Z)().getSharedSecret,n=function(){var n=(0,o.Z)((0,r.Z)().mark((function n(){var t,o;return(0,r.Z)().wrap((function(n){for(;;)switch(n.prev=n.next){case 0:return t=new s.KU({api:s.Ii.Owner,sharedSecret:e()}),n.next=3,t.profileDefinitionProvider.getProfileDefinitions();case 3:return n.next=5,n.sent.map((function(e){return(0,i.Z)((0,i.Z)({},e),{},{slug:(0,c.V)(e.name)})}));case 5:return o=n.sent,n.abrupt("return",{definitions:o});case 7:case"end":return n.stop()}}),n)})));return function(){return n.apply(this,arguments)}}();return(0,a.useQuery)(["profiles"],(function(){return n()}),{refetchOnMount:!1,refetchOnWindowFocus:!1})}}}]);
//# sourceMappingURL=647.3d03031a.chunk.js.map