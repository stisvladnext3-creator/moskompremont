include $(TOPDIR)/rules.mk
PKG_NAME:=luci-app-mkr-router
PKG_VERSION:=0.1.1
PKG_RELEASE:=1
PKG_LICENSE:=GPL-2.0-or-later
PKGARCH:=all
MKR_SOURCE_DIR:=$(abspath $(CURDIR)/../..)
include $(INCLUDE_DIR)/package.mk

define Package/luci-app-mkr-router
  SECTION:=luci
  CATEGORY:=LuCI
  SUBMENU:=3. Applications
  TITLE:=MosKomRemont updater interface
  DEPENDS:=+luci-base +mkr-router-updater
endef

define Build/Compile
endef

define Package/luci-app-mkr-router/install
	$(CP) $(MKR_SOURCE_DIR)/luci/root/* $(1)/
	$(INSTALL_DIR) $(1)/www
	$(CP) $(MKR_SOURCE_DIR)/luci/htdocs/* $(1)/www/
endef

$(eval $(call BuildPackage,luci-app-mkr-router))
