angular.module('umbraco').controller('TwentyFourDays.Controllers.BlockPreviewController',
    ['$scope', '$sce', '$timeout', 'editorState', 'TwentyFourDays.Resources.PreviewResource',
    function ($scope,$sce, $timeout, editorState, previewResource) {
      $scope.language = editorState.getCurrent().variants.find(function (v) {
        return v.active;
      }).language?.culture;

      $scope.id = editorState.getCurrent().id;
        $scope.loading = true;
        $scope.markup = $sce.trustAsHtml('Loading preview');
      
      function loadPreview(blockData) {
        $scope.markup = $sce.trustAsHtml('Loading preview');
        $scope.loading = true;

        previewResource.getPreview(blockData, $scope.id, $scope.language).then(function (data) {
          $scope.markup = $sce.trustAsHtml(data);
          $scope.loading = false;
        });
      }

      var timeoutPromise;

      $scope.$watch('block.data', function (newValue, oldValue) {
        $timeout.cancel(timeoutPromise);

        timeoutPromise = $timeout(function () {   //Set timeout
          loadPreview(newValue);
        }, 500);
      }, true);
    }
]);
