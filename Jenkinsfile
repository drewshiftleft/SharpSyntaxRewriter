#!groovy

pipeline {
  agent { ubuntu-cd }
  parameters {
    gitParameter branchFilter: 'origin/(.*)', defaultValue: 'master', name: 'LIB_TAG', type: 'PT_TAG'
  }
  options {
    timeout(time: 1, unit: 'hours') 
  }
  stages {
    stage('clone') {
      steps {
        script {
          checkout([
            $class: 'GitSCM', 
               branches: [[name: "${LIB_TAG}"]],
               userRemoteConfigs: [[
                 credentialsId: '4b3482c3-735f-4c31-8d1b-d8d3bd889348',
                 url: "ssh://git@${env.REPO_NAME}"
               ]]
          ])
        }
      }
    }
    stage('build') {
      steps {
        sh 'dotnet build -c Release src/SharpSyntaxRewriter/SharpSyntaxRewriter.csproj'
      }
    }
    stage('package') {
      steps {
        sh 'dotnet pack -c Release src/SharpSyntaxRewriter/SharpSyntaxRewriter.csproj -o .   // (note the dot at the end, for current dir)'
      }
    }
    stage('push') {
      steps {
        sh 'nuget push 'SharpSyntaxRewriter.{LIB_TAG}.nupkg' -Source Artifactory'
      }
    }
  }
  post {
    failure {
      script {
        slackSend (channel: '#dev-null', color: '#FF0000', message: "FAILED: (${env.BUILD_URL})")
      }
    }
  }
}
